using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class UnitySimpleWebServer : MonoBehaviour
{
    [Header("伺服器設定")]
    [SerializeField] private int port = 8080;      
    [SerializeField] private string htmlFileName = "index.html"; 

    private HttpListener _listener;
    private bool _isRunning = false;

    // 使用執行緒安全的佇列，用來暫存從手機傳進來的訂單請求
    private struct OrderTask
    {
        public DeliveryGameManager.ExternalOrderRequest Request;
        // 結果為新訂單的 orderId（成功，>= 1）或 -1（失敗）
        public TaskCompletionSource<int> CompletionSource;
    }

    // ➔ 將原本的佇列改成這個包裝後的結構
    private ConcurrentQueue<OrderTask> _incomingRequests = new ConcurrentQueue<OrderTask>();
    // private ConcurrentQueue<DeliveryGameManager.ExternalOrderRequest> _incomingRequests = new ConcurrentQueue<DeliveryGameManager.ExternalOrderRequest>();

    // ➔ 狀態查詢佇列：讓背景執行緒把查詢轉給主執行緒處理，避免在背景執行緒存取 Unity API
    private struct StatusTask
    {
        public int OrderId;
        public TaskCompletionSource<DeliveryGameManager.OrderStatusInfo> CompletionSource;
    }
    private ConcurrentQueue<StatusTask> _pendingStatusQueries = new ConcurrentQueue<StatusTask>();

    void Start()
    {
        StartServer();
    }

    // void Update()
    // {
    //     // ➔ 每幀在 Unity 主執行緒中檢查是否有來自手機的訂單
    //     while (_incomingRequests.TryDequeue(out var request))
    //     {
    //         if (DeliveryGameManager.Instance != null)
    //         {
    //             bool success = DeliveryGameManager.Instance.AddOrderFromExternal(request);
    //             Debug.Log($"[Web Server] 處理外部訂單: {request.foodName} (Index: {request.destinationIndex}) -> 建立結果: {success}");
    //         }
    //     }
    // }
    void Update()
    {
        while (_incomingRequests.TryDequeue(out var task))
        {
            int orderId = -1;
            if (DeliveryGameManager.Instance != null)
            {
                // 在主執行緒拿到真正的建立結果 (orderId >= 1 表示成功，-1 表示失敗)
                orderId = DeliveryGameManager.Instance.AddOrderFromExternal(task.Request);
            }
            // ➔ 通知正在等待回應的背景執行緒：主執行緒已經做完了！
            task.CompletionSource.SetResult(orderId);
        }

        // ➔ 在主執行緒處理狀態查詢（需要存取 Unity API，不能在背景執行緒呼叫）
        while (_pendingStatusQueries.TryDequeue(out var statusTask))
        {
            DeliveryGameManager.OrderStatusInfo result;
            if (DeliveryGameManager.Instance != null)
            {
                result = DeliveryGameManager.Instance.GetOrderStatusInfo(statusTask.OrderId);
            }
            else
            {
                result = new DeliveryGameManager.OrderStatusInfo { found = false, state = "NotFound" };
            }
            statusTask.CompletionSource.SetResult(result);
        }
    }

    void OnDestroy()
    {
        StopServer();
    }

    private void StartServer()
    {
        if (_listener != null && _listener.IsListening) return;
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{port}/");
            _listener.Start();
            _isRunning = true;
            Debug.Log($"[Web Server] 伺服器已啟動，監聽 Port: {port}");
            _ = ListenLoop();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Web Server] 啟動失敗: {ex.Message}");
        }
    }

    private async Task ListenLoop()
    {
        while (_isRunning && _listener.IsListening)
        {
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                _ = ProcessRequestAsync(context);
            }
            catch (HttpListenerException) { break; }
            catch (Exception ex) { Debug.LogError($"[Web Server] 錯誤: {ex.Message}"); }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        string localPath = request.Url.LocalPath;

        // 1. 優先處理手機發過來的訂單 POST 請求
        if (request.HttpMethod == "POST" && localPath == "/submitOrder")
        {
            try
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string jsonString = await reader.ReadToEndAsync();
                    DeliveryGameManager.ExternalOrderRequest apiData = JsonUtility.FromJson<DeliveryGameManager.ExternalOrderRequest>(jsonString);

                    // ➔ 建立一個非同步等待訊號
                    var tcs = new TaskCompletionSource<int>();
                    _incomingRequests.Enqueue(new OrderTask { Request = apiData, CompletionSource = tcs });

                    // ➔ 【核心改變】：背景執行緒會在這裡暫停等待，直到 Update() 幫你跑完拿到結果
                    int createdOrderId = await tcs.Task;
                    bool isOrderCreated = createdOrderId >= 1;

                    if (isOrderCreated)
                    {
                        response.StatusCode = (int)HttpStatusCode.OK;
                        byte[] okBuffer = Encoding.UTF8.GetBytes("{\"status\":\"success\",\"orderId\":" + createdOrderId + "}");
                        response.ContentType = "application/json";
                        response.ContentLength64 = okBuffer.Length;
                        await response.OutputStream.WriteAsync(okBuffer, 0, okBuffer.Length);
                    }
                    else
                    {
                        // ➔ 如果 GameManager 回傳 false (被過濾機制擋掉)，就給手機發送 400 Bad Request
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        byte[] errBuffer = Encoding.UTF8.GetBytes("{\"status\":\"rejected\", \"reason\":\"Duplicate order or station occupied.\"}");
                        response.ContentType = "application/json";
                        response.ContentLength64 = errBuffer.Length;
                        await response.OutputStream.WriteAsync(errBuffer, 0, errBuffer.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                byte[] errBuffer = Encoding.UTF8.GetBytes(ex.Message);
                await response.OutputStream.WriteAsync(errBuffer, 0, errBuffer.Length);
            }
            finally
            {
                response.OutputStream.Close();
            }
            return;
        }

        // 1.5 處理手機輪詢訂單/玩家位置的 GET 請求
        if (request.HttpMethod == "GET" && localPath == "/orderStatus")
        {
            try
            {
                string orderIdParam = request.QueryString["orderId"];
                int orderId;

                if (!int.TryParse(orderIdParam, out orderId) || DeliveryGameManager.Instance == null)
                {
                    DeliveryGameManager.OrderStatusInfo notFound = new DeliveryGameManager.OrderStatusInfo
                    {
                        found = false,
                        state = "NotFound"
                    };
                    string notFoundJson = JsonUtility.ToJson(notFound);
                    byte[] notFoundBuffer = Encoding.UTF8.GetBytes(notFoundJson);
                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.ContentType = "application/json";
                    response.ContentLength64 = notFoundBuffer.Length;
                    await response.OutputStream.WriteAsync(notFoundBuffer, 0, notFoundBuffer.Length);
                }
                else
                {
                    // ➔ 把查詢轉給主執行緒執行，避免在背景執行緒存取 transform.position 等 Unity API
                    var tcs = new TaskCompletionSource<DeliveryGameManager.OrderStatusInfo>();
                    _pendingStatusQueries.Enqueue(new StatusTask { OrderId = orderId, CompletionSource = tcs });
                    DeliveryGameManager.OrderStatusInfo info = await tcs.Task;
                    string json = JsonUtility.ToJson(info);
                    byte[] buffer = Encoding.UTF8.GetBytes(json);
                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                byte[] errBuffer = Encoding.UTF8.GetBytes(ex.Message);
                await response.OutputStream.WriteAsync(errBuffer, 0, errBuffer.Length);
            }
            finally
            {
                response.OutputStream.Close();
            }
            return;
        }

        // 2. 處理靜態檔案讀取 (HTML, PNG, JPG 等)
        // 如果瀏覽器只輸入根目錄 "/"，預設指引到 index.html
        string targetFileName = localPath == "/" ? htmlFileName : localPath.TrimStart('/');
        string filePath = Path.Combine(Application.streamingAssetsPath, targetFileName);

        if (File.Exists(filePath))
        {
            try
            {
                // ➔ 【核心修正】：根據副檔名動態決定 ContentType
                string ext = Path.GetExtension(filePath).ToLower();
                if (ext == ".html" || ext == ".htm") response.ContentType = "text/html; charset=utf-8";
                else if (ext == ".png") response.ContentType = "image/png";
                else if (ext == ".jpg" || ext == ".jpeg") response.ContentType = "image/jpeg";
                else if (ext == ".css") response.ContentType = "text/css";
                else if (ext == ".js") response.ContentType = "application/javascript";
                else response.ContentType = "application/octet-stream";

                // 讀取檔案並轉成 Byte 陣列回傳（不論是文字還是二進位圖片都適用）
                byte[] responseBuffer = await File.ReadAllBytesAsync(filePath);
                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentLength64 = responseBuffer.Length;
                await response.OutputStream.WriteAsync(responseBuffer, 0, responseBuffer.Length);
            }
            catch (Exception ex)
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                byte[] errBuffer = Encoding.UTF8.GetBytes($"<html><body><h3>500 Internal Server Error</h3><p>{ex.Message}</p></body></html>");
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = errBuffer.Length;
                await response.OutputStream.WriteAsync(errBuffer, 0, errBuffer.Length);
            }
        }
        else
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            byte[] notFoundMsg = Encoding.UTF8.GetBytes($"<html><body><h3>404 Not Found</h3><p>File not found: {targetFileName}</p></body></html>");
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = notFoundMsg.Length;
            await response.OutputStream.WriteAsync(notFoundMsg, 0, notFoundMsg.Length);
        }

        response.OutputStream.Close();
    }

    private void StopServer()
    {
        _isRunning = false;
        if (_listener != null)
        {
            if (_listener.IsListening) _listener.Stop();
            _listener.Close();
            _listener = null;
        }
    }
}