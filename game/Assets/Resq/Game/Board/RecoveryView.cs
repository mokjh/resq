using System.Collections;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// U20 aftercare, made visible: once everyone is home the valley recovers on the table. Over a few seconds the
    /// flood recedes to the mission's starting water, fire, lava and runout overlays fade, every rescued resident
    /// cheers, and the light warms a little. Presentation only: the simulation is untouched, and leaving aftercare
    /// restores the live view at once. Reduced effects skips straight to the recovered state.
    /// </summary>
    public sealed class RecoveryView : MonoBehaviour
    {
        public BoardView Board;
        public float Seconds = 3.5f;
        public float Progress { get; private set; }

        Coroutine _run;
        Light _sun;
        float _sunIntensity;
        Color _sunColor, _sky;
        bool _captured;

        public void Play()
        {
            Stop();
            _sun = RenderSettings.sun;
            if (_sun != null) { _sunIntensity = _sun.intensity; _sunColor = _sun.color; }
            _sky = RenderSettings.ambientSkyColor;
            _captured = true;
            if (Board.Actors != null) Board.Actors.Celebrate = true;
            _run = StartCoroutine(Run());
        }

        public void Stop()
        {
            if (_run != null) { StopCoroutine(_run); _run = null; }
            if (Progress > 0f || _captured) Apply(0f);
            if (Board != null && Board.Actors != null) Board.Actors.Celebrate = false;
            _captured = false;
        }

        IEnumerator Run()
        {
            float duration = Comfort.ReducedEffects ? 0f : Seconds;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                Apply(Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }
            Apply(1f);
            _run = null;
        }

        void Apply(float k)
        {
            Progress = k;
            if (Board == null) return;
            if (Board.Water != null) Board.Water.Recede = k;
            if (Board.Hazards != null) Board.Hazards.Fade = k;
            if (!_captured) return;
            if (_sun != null)
            {
                _sun.intensity = _sunIntensity * (1f + 0.15f * k);
                _sun.color = Color.Lerp(_sunColor, Color.Lerp(_sunColor, new Color(1f, 0.93f, 0.8f), 0.6f), k);
            }
            RenderSettings.ambientSkyColor = Color.Lerp(_sky, _sky * 1.15f, k);
        }
    }
}
