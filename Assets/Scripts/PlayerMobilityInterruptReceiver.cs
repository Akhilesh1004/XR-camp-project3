using System.Collections;
using UnityEngine;

public class PlayerMobilityInterruptReceiver : MonoBehaviour
{
    [Header("玩家 Rigidbody")]
    public Rigidbody playerRigidbody;

    [Header("要中斷的能力")]
    public WebSwinger[] webSwingers;
    public WallGrabber[] wallGrabbers;

    [Header("中斷設定")]
    [Tooltip("被爆炸打到後，暫時禁用蛛絲與爬牆多久。0 代表只中斷當下，不禁用輸入")]
    public float defaultDisableDuration = 0.6f;

    [Tooltip("爆炸瞬間是否清掉玩家速度。建議先 false")]
    public bool clearVelocityOnInterrupt = false;

    private Coroutine disableRoutine;

    void Awake()
    {
        if (playerRigidbody == null)
        {
            playerRigidbody = GetComponentInParent<Rigidbody>();
        }
    }

    public void InterruptMobility()
    {
        InterruptMobility(defaultDisableDuration, clearVelocityOnInterrupt);
    }

    public void InterruptMobility(float disableDuration, bool clearVelocity)
    {
        if (webSwingers != null)
        {
            foreach (WebSwinger swinger in webSwingers)
            {
                if (swinger != null)
                {
                    swinger.ForceStopSwing(false);
                }
            }
        }

        if (wallGrabbers != null)
        {
            foreach (WallGrabber grabber in wallGrabbers)
            {
                if (grabber != null)
                {
                    grabber.ForceReleaseGrab(false);
                }
            }
        }

        WallGrabber.ForceReleaseAll(false);

        if (clearVelocity && playerRigidbody != null)
        {
            playerRigidbody.velocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
        }

        if (disableDuration > 0f)
        {
            if (disableRoutine != null)
            {
                StopCoroutine(disableRoutine);
            }

            disableRoutine = StartCoroutine(TemporarilyDisableAbilities(disableDuration));
        }
    }

    IEnumerator TemporarilyDisableAbilities(float duration)
    {
        SetAbilitiesEnabled(false);

        yield return new WaitForSeconds(duration);

        SetAbilitiesEnabled(true);
        disableRoutine = null;
    }

    void SetAbilitiesEnabled(bool enabled)
    {
        if (webSwingers != null)
        {
            foreach (WebSwinger swinger in webSwingers)
            {
                if (swinger != null)
                {
                    swinger.enabled = enabled;
                }
            }
        }

        if (wallGrabbers != null)
        {
            foreach (WallGrabber grabber in wallGrabbers)
            {
                if (grabber != null)
                {
                    grabber.enabled = enabled;
                }
            }
        }
    }
}