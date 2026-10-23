using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>One selectable control on a menu card.</summary>
    public struct MenuOption
    {
        public string Label;
        public string Detail;      // optional second line (stars, state)
        public Action OnPress;
        public bool Primary, Disabled, Selected;
        public Icon? Icon;

        public static MenuOption Button(string label, Action onPress, bool primary = false, string detail = null) =>
            new MenuOption { Label = label, OnPress = onPress, Primary = primary, Detail = detail };
        public static MenuOption Locked(string label, string detail) => new MenuOption { Label = label, Detail = detail, Disabled = true, Icon = Game.Icon.Ring };
    }

    /// <summary>What a screen shows: a title, a short body and a grid of controls (plus an optional back row).</summary>
    public sealed class MenuSpec
    {
        public string Id = "";          // the U-state id (U06 …), for tests and logs
        public string Title = "", Subtitle = "", Body = "";
        public Icon? TitleIcon;
        public List<MenuOption> Items = new List<MenuOption>();
        public int Columns = 1;
        public List<MenuOption> Footer = new List<MenuOption>();   // Back / Cancel row, always last
    }

    /// <summary>
    /// The menu card: a glass panel floating above the board's centre, facing the seated player, built from a
    /// <see cref="MenuSpec"/>. Every control is a <see cref="Pickable"/> button, so menus work with the same inputs as
    /// play (hands, either hand alone, head dwell, mouse) through the one interaction layer. Text follows the
    /// player's text scale; the card grows to fit rather than clipping.
    /// </summary>
    public sealed class MenuPanel : MonoBehaviour
    {
        public Material Glass;
        public MenuSpec Current { get; private set; }
        /// <summary>Height of the open card in metres (0 when closed); the card stands on this transform.</summary>
        public float Height { get; private set; }
        public float Width = 0.40f;
        /// <summary>Set by settings: 1, 1.25 or 1.5.</summary>
        public static float TextScale = 1f;
        public static bool HighContrast;

        Transform _root;

        public void Show(MenuSpec spec)
        {
            Current = spec;
            if (_root == null) _snap = true;   // a freshly opened card starts facing the player
            Discard();
            if (Glass == null) Glass = new Material(Shader.Find("Resq/Glass"));
            _root = new GameObject("Menu_" + spec.Id).transform;
            _root.SetParent(transform, false);
            Build(spec);
            GlassBatcher.Attach(_root, Glass);
        }

        public void Hide()
        {
            Current = null; Height = 0;
            Discard();
        }

        /// <summary>Hide the old card at once (Destroy only lands at the end of the frame), then destroy it.</summary>
        void Discard()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(false);
            Destroy(_root.gameObject);
            _root = null;
        }

        public bool Visible => _root != null;

        /// <summary>
        /// The card turns to face the player's head (yaw and pitch), easing rather than snapping, so text is never
        /// read at a grazing angle and small head movements don't make it swim.
        /// </summary>
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || _root == null) return;
            var dir = transform.position - cam.transform.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
            float k = _snap ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, k);
            _snap = false;
        }
        bool _snap = true;

        void Build(MenuSpec spec)
        {
            float k = TextScale;
            float w = Width * Mathf.Lerp(1f, 1.18f, (k - 1f) * 2f);
            float pad = 0.016f, inner = w - pad * 2;
            float titleSize = 0.2f * k, subSize = 0.11f * k, bodySize = 0.115f * k, btnSize = 0.11f * k;
            float btnH = 0.034f * Mathf.Max(1f, k * 0.92f) + (HasDetail(spec) ? 0.012f * k : 0);
            float gap = 0.008f;

            // measure text blocks so the card fits its content
            float titleH = Measure(spec.Title, titleSize, inner - (spec.TitleIcon.HasValue ? 0.03f : 0), true);
            float subH = spec.Subtitle.Length > 0 ? Measure(spec.Subtitle, subSize, inner, false) : 0;
            float bodyH = spec.Body.Length > 0 ? Measure(spec.Body, bodySize, inner, false) : 0;
            int cols = Mathf.Max(1, spec.Columns);
            int rows = (spec.Items.Count + cols - 1) / cols;
            float gridH = rows > 0 ? rows * btnH + (rows - 1) * gap : 0;
            float footH = spec.Footer.Count > 0 ? 0.03f * Mathf.Max(1f, k * 0.92f) : 0;
            float h = pad + titleH + (subH > 0 ? subH + 0.004f : 0) + (bodyH > 0 ? bodyH + 0.01f : 0) + (gridH > 0 ? gridH + 0.012f : 0) + (footH > 0 ? footH + 0.01f : 0) + pad;

            var fill = HighContrast ? new Color(0.02f, 0.03f, 0.035f, 0.97f) : UiKit.GlassFill;
            // the card stands on its bottom edge, so a taller screen grows upward, away from the valley's hills
            var card = UiKit.Panel(_root, "Card", new Vector3(0, h * 0.5f, 0), w, h, Glass, 0.018f, fill);
            Height = h;
            float y = h * 0.5f - pad;
            float x0 = -w * 0.5f + pad;

            if (spec.TitleIcon.HasValue)
                UiKit.IconObj(card, spec.TitleIcon.Value, new Vector3(x0 + 0.012f, y - titleH * 0.5f, 0), 0.024f, UiKit.Accent, Glass);
            float tx = spec.TitleIcon.HasValue ? 0.03f : 0;
            var title = Block(card, spec.Title, x0 + tx, y, inner - tx, titleH, titleSize, true, UiKit.Ink);
            title.name = "Title";
            y -= titleH;
            if (subH > 0) { y -= 0.004f; Block(card, spec.Subtitle, x0, y, inner, subH, subSize, false, UiKit.Accent).name = "Subtitle"; y -= subH; }
            if (bodyH > 0) { y -= 0.01f; Block(card, spec.Body, x0, y, inner, bodyH, bodySize, false, HighContrast ? UiKit.Ink : UiKit.InkSoft).name = "Body"; y -= bodyH; }
            if (gridH > 0)
            {
                y -= 0.012f;
                float bw = (inner - (cols - 1) * gap) / cols;
                for (int i = 0; i < spec.Items.Count; i++)
                {
                    int r = i / cols, c = i % cols;
                    var pos = new Vector3(x0 + bw * 0.5f + c * (bw + gap), y - btnH * 0.5f - r * (btnH + gap), 0);
                    MakeButton(card, spec.Items[i], pos, bw, btnH, btnSize);
                }
                y -= gridH;
            }
            if (footH > 0)
            {
                y -= 0.01f;
                int n = spec.Footer.Count;
                float fw = Mathf.Min(0.12f * k, (inner - (n - 1) * gap) / n);
                float start = -(n - 1) * (fw + gap) * 0.5f;
                for (int i = 0; i < n; i++)
                    MakeButton(card, spec.Footer[i], new Vector3(start + i * (fw + gap), y - footH * 0.5f, 0), fw, footH, btnSize * 0.95f);
            }
        }

        static bool HasDetail(MenuSpec s) { foreach (var i in s.Items) if (!string.IsNullOrEmpty(i.Detail)) return true; return false; }

        float Measure(string text, float size, float width, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var probe = UiKit.Text(transform, text, Vector3.zero, size, UiKit.Ink, bold, width, 1f);
            probe.enableWordWrapping = true;
            probe.overflowMode = TextOverflowModes.Overflow;
            float hgt = probe.GetPreferredValues(text, width, 0).y;
            DestroyImmediate(probe.gameObject);
            return Mathf.Max(hgt, size * 0.12f);
        }

        static TextMeshPro Block(Transform parent, string text, float left, float top, float width, float height, float size, bool bold, Color color)
        {
            var t = UiKit.Text(parent, text, new Vector3(left + width * 0.5f, top - height * 0.5f, 0), size, color, bold, width, height);
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.TopLeft;
            return t;
        }

        void MakeButton(Transform parent, MenuOption item, Vector3 pos, float w, float h, float size)
        {
            Color? fill = item.Disabled ? new Color(0.2f, 0.22f, 0.24f, 0.55f)
                : item.Primary ? new Color(1f, 0.42f, 0.21f, 0.94f)
                : item.Selected ? new Color(0.20f, 0.55f, 0.60f, 0.92f)
                : HighContrast ? new Color(0.16f, 0.2f, 0.22f, 0.98f) : new Color(1f, 1f, 1f, 0.12f);
            var root = UiKit.Panel(parent, "Button_" + item.Label, pos, w, h, Glass, Mathf.Min(0.012f, h * 0.4f), fill);
            float textLeft = 0;
            if (item.Icon.HasValue)
            {
                UiKit.IconObj(root, item.Icon.Value, new Vector3(-w * 0.5f + 0.014f, 0, 0), 0.016f, item.Disabled ? UiKit.InkSoft : UiKit.Accent, Glass);
                textLeft = 0.02f;
            }
            string text = string.IsNullOrEmpty(item.Detail) ? item.Label : $"<b>{item.Label}</b>\n<size=78%>{item.Detail}</size>";
            var label = UiKit.Text(root, text, new Vector3(textLeft * 0.5f, 0, 0), size, item.Disabled ? new Color(1, 1, 1, 0.45f) : UiKit.Ink,
                                   string.IsNullOrEmpty(item.Detail), w - 0.008f - textLeft, h - 0.004f, TextAlignmentOptions.Center);
            label.name = "Label";
            label.enableAutoSizing = true; label.fontSizeMax = size; label.fontSizeMin = size * 0.75f;
            label.lineSpacing = -8f;
            label.enableWordWrapping = true;   // "Alder Valley" wraps onto two lines rather than shrinking
            if (item.Disabled) return;
            var col = root.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(w, h, 0.012f); col.isTrigger = true;
            var p = root.gameObject.AddComponent<Pickable>(); p.Kind = PickKind.Button; p.OnPress = item.OnPress; p.Label = item.Label;
        }
    }
}
