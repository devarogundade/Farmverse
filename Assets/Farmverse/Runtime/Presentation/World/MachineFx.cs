#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// A machine at work: parts named "Spin…" turn (sails, wheels, saw blades), a
    /// "Chimney" puffs smoke, and the building hums with a slight shake.
    /// </summary>
    public sealed class MachineFx : MonoBehaviour, IVisualStateReceiver
    {
        [SerializeField] private float spinDegreesPerSecond = 90f;

        private readonly List<(Transform Part, Vector3 Axis)> _spinners = new List<(Transform, Vector3)>();
        private ParticleSystem? _smoke;
        private bool _working;
        private bool _reduceMotion;
        private Transform? _body;

        private void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Spin", StringComparison.Ordinal)) _spinners.Add((t, AxisOf(t)));
                else if (t.name == "Chimney") _smoke = Smoke(t);
            }
            _body = StagedVisual.ModelOf(transform);
        }

        public void OnVisualState(WorldVisual visual, bool reduceMotion)
        {
            _reduceMotion = reduceMotion;
            _working = visual.Stage == VisualStage.Working;
            if (_smoke == null) return;
            if (_working && !_smoke.isPlaying) _smoke.Play();
            else if (!_working && _smoke.isPlaying) _smoke.Stop();
        }

        private void Update()
        {
            if (!_working || _reduceMotion) return;
            foreach (var (part, axis) in _spinners) part.Rotate(axis, spinDegreesPerSecond * Time.deltaTime, Space.Self);
            if (_body != null) _body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(Time.time * 18f)) * 0.008f, 0);
        }

        /// <summary>Sails, wheels and blades are flat: they turn about their thinnest dimension.</summary>
        private static Vector3 AxisOf(Transform part)
        {
            var mesh = part.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return Vector3.forward;
            var size = mesh.bounds.size;
            if (size.x <= size.y && size.x <= size.z) return Vector3.right;
            return size.y <= size.z ? Vector3.up : Vector3.forward;
        }

        private static ParticleSystem Smoke(Transform at)
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(at, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 2.4f;
            main.startSpeed = 0.35f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.maxParticles = 30;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 5f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.05f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0.7f, 0), new GradientAlphaKey(0f, 1) });
            colour.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
            return ps;
        }
    }
}
