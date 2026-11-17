using UnityEngine;

namespace PentaWay
{
    [RequireComponent(typeof(RectTransform))]
    public class UIPanelAnimator : MonoBehaviour
    {
        public float duration = 0.2f;
        public float slideDistance = 60f;

        CanvasGroup group;
        RectTransform rect;
        Vector2 basePosition;
        bool visible;
        float progress;

        public bool IsShown => visible;

        public static UIPanelAnimator Attach(GameObject panel, float slide)
        {
            var animator = panel.AddComponent<UIPanelAnimator>();
            animator.slideDistance = slide;
            animator.group = panel.GetComponent<CanvasGroup>();
            if (animator.group == null) animator.group = panel.AddComponent<CanvasGroup>();
            animator.rect = (RectTransform)panel.transform;
            animator.basePosition = animator.rect.anchoredPosition;
            animator.visible = panel.activeSelf;
            animator.progress = animator.visible ? 1f : 0f;
            return animator;
        }

        public void Show(bool show)
        {
            if (show == visible && show == gameObject.activeSelf) return;
            visible = show;
            if (show && !gameObject.activeSelf)
            {
                progress = 0f;
                gameObject.SetActive(true);
                Apply();
            }
            group.blocksRaycasts = show;
            group.interactable = show;
        }

        void Update()
        {
            float target = visible ? 1f : 0f;
            if (Mathf.Approximately(progress, target))
            {
                if (!visible && gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }
            progress = Mathf.MoveTowards(progress, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, duration));
            Apply();
        }

        void Apply()
        {
            float eased = 1f - (1f - progress) * (1f - progress);
            group.alpha = eased;
            rect.anchoredPosition = basePosition + Vector2.down * slideDistance * (1f - eased);
        }
    }
}
