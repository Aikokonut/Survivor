using UnityEngine;
using UnityEngine.UI;

namespace Stickman
{
    public class FPSControlller : MonoBehaviour
    {
        [SerializeField] int targetFrameRate = 60;
        [SerializeField] Text fpsText;

        float timer;
        int frameCount;
        int lastShownFps = -1;

        void Start()
        {
            Application.targetFrameRate = targetFrameRate;
        }

        void Update()
        {
            frameCount++;
            timer += Time.unscaledDeltaTime;

            if (timer < 0.5f) return;

            int fps = (int)(frameCount / timer + 0.5f);
            frameCount = 0;
            timer = 0f;

            if (fpsText == null || fps == lastShownFps) return;
            lastShownFps = fps;
            fpsText.text = fps.ToString();
        }
    }
}
