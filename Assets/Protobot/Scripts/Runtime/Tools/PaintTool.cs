using System.Collections.Generic;
using Protobot.Audio;
using Protobot.Interaction;
using Protobot.Menus;
using Protobot.Parts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools
{
    /// <summary>
    /// Spray paint gun. Aim at a part and left click to paint the whole part; Shift + click strips the paint
    /// back to the original finish. Right click on a part opens its colours (a preview of the part and one
    /// swatch per material, each editable); right click anywhere else picks the gun's colour (colour square,
    /// RGB, hex, team colours and the last 20 used). The paint cup on the gun shows the loaded colour.
    /// </summary>
    public class PaintTool : PlayerTool
    {
        const int MaxRecent = 20;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] ColorPickerMenu picker;
        [SerializeField] Sprite crosshair;
        [SerializeField] Color color = new(0.78f, 0.12f, 0.1f);

        static readonly List<Color> recent = new();
        ParticleSystem spray;
        MaterialPropertyBlock block;

        public Color CurrentColor => color;
        public static IReadOnlyList<Color> Recent => recent;

        public override string ControlsHint => "Left click: paint part   ·   Shift + click: remove paint   ·   Right click a part: edit its colors   ·   Right click empty space: spray color";

        public override void OnEquip()
        {
            ShowColorOnGun();
            if (spray == null)
                spray = BuildSpray();
        }

        public override void SecondaryTap() => OpenPicker();
        public override void SecondaryHold() => OpenPicker();

        void OpenPicker()
        {
            // On a part: that part's colours. Anywhere else: the colour loaded in the gun.
            var target = Cursor.Current;
            var part = target != null && Cursor.CurrentInReach ? target.GetComponentInParent<PartBody>() : null;
            if (part != null && part.TryGetComponent(out HoverTarget hover))
            {
                picker.OpenForPart(hover, PartShape.DetailRenderers(part.gameObject), recent, painted =>
                {
                    PartModifications.On(part.gameObject).RecordPaint(painted);
                    foreach (var pair in painted.Paints)
                        Remember(pair.Value);
                    Spray(part.transform.position, painted.Paint ?? color);
                    PlaySound(Sounds.Spray, 0.45f);
                    Hud.ShowToast($"Repainted {part.name}", 1.5f);
                });
                return;
            }
            picker.Open(color, recent, c =>
            {
                color = c;
                ShowColorOnGun();
                Remember(c);
            });
        }

        public override void PrimaryDown()
        {
            var target = Cursor.Current;
            if (target == null || !Cursor.CurrentInReach)
                return;
            var part = target.GetComponentInParent<PartBody>();
            if (part == null || !part.TryGetComponent(out HoverTarget hover))
            {
                Hud.ShowError("Spray paint works on parts");
                return;
            }

            var strip = Keyboard.current.shiftKey.isPressed;
            hover.SetPaint(strip ? null : color);
            PartModifications.On(part.gameObject).RecordPaint(hover);
            if (!strip)
                Remember(color);
            Spray(Cursor.CurrentHit.point, strip ? new Color(0.85f, 0.85f, 0.85f) : color);
            PlaySound(Sounds.Spray, 0.45f);
        }

        public override bool DescribeTarget(HoverTarget target, bool inReach, ref CursorView view)
        {
            view.reticle = crosshair;
            view.large = true;
            view.reticleColor = color;
            if (target == null)
                return true;
            if (target.GetComponentInParent<PartBody>() == null)
            {
                view.hint = "Spray paint works on parts";
                view.available = false;
                return true;
            }
            if (!inReach)
                return true;
            var painted = target.Paint;
            if (Keyboard.current.shiftKey.isPressed)
            {
                view.hint = painted != null ? "Click to strip the paint" : "Not painted";
                view.available = painted != null;
            }
            else if (painted != null && Approximately(painted.Value, color))
            {
                view.hint = "Already this color   ·   Shift + click: strip paint";
            }
            else
            {
                view.hint = painted != null ? "Click to repaint   ·   Right click: this part's colors" : "Click to paint   ·   Right click: this part's colors";
            }
            return true;
        }

        static void Remember(Color c)
        {
            recent.RemoveAll(r => Approximately(r, c));
            recent.Insert(0, c);
            if (recent.Count > MaxRecent)
                recent.RemoveAt(recent.Count - 1);
        }

        static bool Approximately(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

        /// <summary>Tints the paint cup (any renderer named "PaintChamber") on the gun.</summary>
        void ShowColorOnGun()
        {
            if (ViewModel == null)
                return;
            block ??= new MaterialPropertyBlock();
            foreach (var r in ViewModel.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.name.Contains("PaintChamber"))
                    continue;
                block.SetColor(BaseColor, color);
                r.SetPropertyBlock(block);
            }
        }

        void Spray(Vector3 target, Color c)
        {
            var origin = View.transform.position + View.transform.right * 0.12f - View.transform.up * 0.1f + View.transform.forward * 0.25f;
            spray.transform.position = origin;
            spray.transform.rotation = Quaternion.LookRotation(target - origin);
            var main = spray.main;
            main.startColor = new Color(c.r, c.g, c.b, 0.55f);
            main.startSpeed = Vector3.Distance(origin, target) * 2.4f;
            spray.Emit(60);
        }

        ParticleSystem BuildSpray()
        {
            var go = new GameObject("Paint Spray");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = 0.4f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.012f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 7f;
            shape.radius = 0.002f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = ToolMaterials.SoftParticle;
            return ps;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => recent.Clear();
    }
}
