using UnityEngine;

namespace PentaWay
{
    public class UIPulse : MonoBehaviour
    {
        const float Duration = 0.25f;
        const float Depth = 0.06f;

        float elapsed = Duration;

        public static void Play(Component target)
        {
            if (target == null) return;
            UIPulse pulse = target.GetComponent<UIPulse>();
            if (pulse == null) pulse = target.gameObject.AddComponent<UIPulse>();
            pulse.elapsed = 0f;
            pulse.enabled = true;
        }

        void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Duration);
            transform.localScale = Vector3.one * (1f - Depth * Mathf.Sin(t * Mathf.PI));
            if (t >= 1f)
            {
                transform.localScale = Vector3.one;
                enabled = false;
            }
        }
    }
}
