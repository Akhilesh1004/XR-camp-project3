using UnityEngine;

/// <summary>
/// 用來把 Unity 世界座標 (X, Z) 轉換成網頁地圖圖片 (NewMap.png) 上的像素座標。
/// 做法：在場景中指定數個「世界座標 Transform」與其對應的「地圖像素座標」，
/// 透過最小平方法計算出一組仿射轉換 (affine transform)，
/// 之後即可用 TryWorldToImage() 把任意世界座標（例如玩家位置）換算成地圖像素座標。
///
/// 設定方式：
/// 1. 在 calibrationPoints 中，將 worldPoint 指向場景中對應 index.html / tracking.html
///    locations (A~F) 的實際世界座標物件（例如 destinationPoints 或其他標記點）。
/// 2. imageCoord 預設值已經對應 index.html 中 locations 的 coords，
///    若日後地圖圖片或標記座標調整，請同步修改這裡的數值。
/// 3. 至少需要 3 個「不共線」的有效校正點，數量越多、分布越廣，換算結果越準確。
/// </summary>
public class WorldToMapCalibration : MonoBehaviour
{
    [System.Serializable]
    public class CalibrationPoint
    {
        [Tooltip("僅用於辨識，不影響計算（建議填 A/B/C/D/E/F 對應 index.html 的地點名稱）")]
        public string label;

        [Tooltip("世界座標中的參考點")]
        public Transform worldPoint;

        [Tooltip("對應在 NewMap.png 上的像素座標 (imageX, imageY)，需與 index.html 的 locations.coords 對應")]
        public Vector2 imageCoord;
    }

    [Header("世界座標 -> 地圖像素座標 校正點")]
    [Tooltip("至少需要 3 個不共線的有效校正點 (worldPoint 不可為空)")]
    public CalibrationPoint[] calibrationPoints = new CalibrationPoint[]
    {
        new CalibrationPoint { label = "A", imageCoord = new Vector2(250, 580) },
        new CalibrationPoint { label = "B", imageCoord = new Vector2(600, 1025) },
        new CalibrationPoint { label = "C", imageCoord = new Vector2(185, 275) },
        new CalibrationPoint { label = "D", imageCoord = new Vector2(550, 325) },
        new CalibrationPoint { label = "E", imageCoord = new Vector2(650, 785) },
        new CalibrationPoint { label = "F", imageCoord = new Vector2(540, 625) },
    };

    // imageX = ax * worldX + bx * worldZ + cx
    // imageY = ay * worldX + by * worldZ + cy
    private float ax, bx, cx;
    private float ay, by, cy;
    private bool matrixValid = false;

    void Awake()
    {
        RecomputeTransform();
    }

    void OnValidate()
    {
        RecomputeTransform();
    }

    /// <summary>
    /// 依目前 calibrationPoints 重新計算世界座標 -> 地圖像素座標的轉換矩陣。
    /// 若有效校正點不足 3 個，轉換會被標記為無效。
    /// </summary>
    [ContextMenu("Recompute Calibration")]
    public void RecomputeTransform()
    {
        matrixValid = false;

        if (calibrationPoints == null) return;

        int n = 0;
        foreach (var p in calibrationPoints)
        {
            if (p != null && p.worldPoint != null) n++;
        }

        if (n < 3) return;

        // 建立最小平方法所需的正規方程式 (Normal Equations)：
        // [Sxx Sxz Sx ] [a]   [Sx_ix]
        // [Sxz Szz Sz ] [b] = [Sz_ix]
        // [Sx  Sz  n  ] [c]   [S_ix]
        // (imageY 的部分用相同的左側矩陣，右側換成 iy)
        double sxx = 0, sxz = 0, szz = 0, sx = 0, sz = 0;
        double sx_ix = 0, sz_ix = 0, s_ix = 0;
        double sx_iy = 0, sz_iy = 0, s_iy = 0;

        foreach (var p in calibrationPoints)
        {
            if (p == null || p.worldPoint == null) continue;

            double wx = p.worldPoint.position.x;
            double wz = p.worldPoint.position.z;
            double ix = p.imageCoord.x;
            double iy = p.imageCoord.y;

            sxx += wx * wx;
            sxz += wx * wz;
            szz += wz * wz;
            sx += wx;
            sz += wz;

            sx_ix += wx * ix;
            sz_ix += wz * ix;
            s_ix += ix;

            sx_iy += wx * iy;
            sz_iy += wz * iy;
            s_iy += iy;
        }

        double[,] m = new double[3, 3]
        {
            { sxx, sxz, sx },
            { sxz, szz, sz },
            { sx,  sz,  n }
        };

        double[] rhsX = { sx_ix, sz_ix, s_ix };
        double[] rhsY = { sx_iy, sz_iy, s_iy };

        if (!Solve3x3(m, rhsX, out double a1, out double b1, out double c1)) return;
        if (!Solve3x3(m, rhsY, out double a2, out double b2, out double c2)) return;

        ax = (float)a1; bx = (float)b1; cx = (float)c1;
        ay = (float)a2; by = (float)b2; cy = (float)c2;
        matrixValid = true;
    }

    /// <summary>
    /// 將世界座標 (使用 X, Z 軸) 轉換成地圖像素座標。
    /// 回傳 false 表示校正資料不足，無法轉換。
    /// </summary>
    public bool TryWorldToImage(Vector3 worldPosition, out Vector2 imageCoord)
    {
        if (!matrixValid)
        {
            imageCoord = Vector2.zero;
            return false;
        }

        float wx = worldPosition.x;
        float wz = worldPosition.z;

        imageCoord = new Vector2(
            ax * wx + bx * wz + cx,
            ay * wx + by * wz + cy
        );
        return true;
    }

    public bool IsCalibrated => matrixValid;

    private static bool Solve3x3(double[,] m, double[] rhs, out double x, out double y, out double z)
    {
        double det =
              m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
            - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
            + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

        if (System.Math.Abs(det) < 1e-9)
        {
            x = y = z = 0;
            return false;
        }

        double[,] mx = (double[,])m.Clone();
        mx[0, 0] = rhs[0]; mx[1, 0] = rhs[1]; mx[2, 0] = rhs[2];
        double detX =
              mx[0, 0] * (mx[1, 1] * mx[2, 2] - mx[1, 2] * mx[2, 1])
            - mx[0, 1] * (mx[1, 0] * mx[2, 2] - mx[1, 2] * mx[2, 0])
            + mx[0, 2] * (mx[1, 0] * mx[2, 1] - mx[1, 1] * mx[2, 0]);

        double[,] my = (double[,])m.Clone();
        my[0, 1] = rhs[0]; my[1, 1] = rhs[1]; my[2, 1] = rhs[2];
        double detY =
              my[0, 0] * (my[1, 1] * my[2, 2] - my[1, 2] * my[2, 1])
            - my[0, 1] * (my[1, 0] * my[2, 2] - my[1, 2] * my[2, 0])
            + my[0, 2] * (my[1, 0] * my[2, 1] - my[1, 1] * my[2, 0]);

        double[,] mz = (double[,])m.Clone();
        mz[0, 2] = rhs[0]; mz[1, 2] = rhs[1]; mz[2, 2] = rhs[2];
        double detZ =
              mz[0, 0] * (mz[1, 1] * mz[2, 2] - mz[1, 2] * mz[2, 1])
            - mz[0, 1] * (mz[1, 0] * mz[2, 2] - mz[1, 2] * mz[2, 0])
            + mz[0, 2] * (mz[1, 0] * mz[2, 1] - mz[1, 1] * mz[2, 0]);

        x = detX / det;
        y = detY / det;
        z = detZ / det;
        return true;
    }
}
