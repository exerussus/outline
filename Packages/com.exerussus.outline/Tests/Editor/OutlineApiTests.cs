using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Exerussus.Outline.Tests
{
    public sealed class OutlineApiTests
    {
        private readonly List<Object> _objects = new();
        private readonly List<OutlineHandle> _handles = new();
        private OutlineStyle _style;

        [SetUp]
        public void SetUp()
        {
            _style = ScriptableObject.CreateInstance<OutlineStyle>();
            _objects.Add(_style);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var h in _handles)
                h.Hide();
            _handles.Clear();
            foreach (var o in _objects)
                if (o != null)
                    Object.DestroyImmediate(o);
            _objects.Clear();
        }

        private Renderer CreateRenderer()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _objects.Add(go);
            return go.GetComponent<Renderer>();
        }

        private OutlineHandle Show(Renderer r)
        {
            var h = OutlineApi.Show(r, _style, OutlineOptions.Default);
            _handles.Add(h);
            return h;
        }

        [Test]
        public void Show_ReturnsAliveHandle()
        {
            var h = Show(CreateRenderer());
            Assert.IsTrue(h.IsAlive);
            Assert.AreEqual(1, OutlineApi.GetRendererCount(h.Slot));
        }

        [Test]
        public void Show_NullStyle_ReturnsInvalid()
        {
            var h = OutlineApi.Show(CreateRenderer(), null, OutlineOptions.Default);
            Assert.IsFalse(h.IsAlive);
            Assert.AreEqual(OutlineHandle.Invalid, h);
        }

        [Test]
        public void Hide_MakesHandleStale_MethodsAreNoOps()
        {
            var h = Show(CreateRenderer());
            h.Hide();
            Assert.IsFalse(h.IsAlive);
            Assert.DoesNotThrow(() =>
            {
                h.SetFade(0.5f);
                h.FadeTo(0f, 1f);
                h.SetStyle(_style);
                h.Hide();
            });
        }

        [Test]
        public void ReusedSlot_OldHandleStaysStale()
        {
            var r = CreateRenderer();
            var h1 = Show(r);
            int slot = h1.Slot;
            h1.Hide();
            var h2 = Show(r);
            Assert.AreEqual(slot, h2.Slot);
            Assert.AreNotEqual(h1, h2);
            Assert.IsFalse(h1.IsAlive);
            Assert.IsTrue(h2.IsAlive);
        }

        [Test]
        public void UnsupportedRenderer_IsSkipped()
        {
            var go = new GameObject("line");
            _objects.Add(go);
            var line = go.AddComponent<LineRenderer>();
            var h = Show(line);
            Assert.IsTrue(h.IsAlive);
            Assert.AreEqual(0, OutlineApi.GetRendererCount(h.Slot));
        }

        [Test]
        public void SharedRenderer_LatestOwns_PreviousRestoredOnHide()
        {
            var r = CreateRenderer();
            var h1 = Show(r);
            var h2 = Show(r);
            Assert.IsTrue(OutlineApi.IsOwner(r, h2.Slot));

            h2.Hide();
            Assert.IsTrue(OutlineApi.IsOwner(r, h1.Slot));

            h1.Hide();
            Assert.IsFalse(OutlineApi.IsOwner(r, h1.Slot));
            Assert.IsFalse(OutlineApi.IsOwner(r, h2.Slot));
        }

        [Test]
        public void Limit_ExtraShowReturnsInvalid()
        {
            var r = CreateRenderer();
            int free = OutlineApi.MaxEntries - OutlineApi.AliveCount;
            for (int i = 0; i < free; i++)
                Assert.IsTrue(Show(r).IsAlive);

            LogAssert.Expect(LogType.Warning, new Regex("лимит"));
            var extra = OutlineApi.Show(r, _style, OutlineOptions.Default);
            Assert.IsFalse(extra.IsAlive);
        }

        [Test]
        public void SetFade_IsImmediate()
        {
            var h = Show(CreateRenderer());
            h.SetFade(0.3f);
            Assert.AreEqual(0.3f, OutlineApi.EvaluateFade(h.Slot, OutlineClock.Now), 1e-5f);
        }

        [Test]
        public void FadeTo_ReachesTargetAfterDuration()
        {
            var h = Show(CreateRenderer());
            h.FadeTo(0f, 0.5f);
            Assert.AreEqual(0f, OutlineApi.EvaluateFade(h.Slot, OutlineClock.Now + 1f), 1e-5f);
        }

        [Test]
        public void FadeIn_StartsFromZero()
        {
            var o = OutlineOptions.Default;
            o.fadeIn = 1f;
            var h = OutlineApi.Show(CreateRenderer(), _style, o);
            _handles.Add(h);
            Assert.AreEqual(0f, OutlineApi.EvaluateFade(h.Slot, OutlineClock.Now), 0.05f);
        }

        [Test]
        public void DestroyedRenderer_HideDoesNotThrow()
        {
            var r = CreateRenderer();
            var h = Show(r);
            Object.DestroyImmediate(r.gameObject);
            Assert.DoesNotThrow(() => h.Hide());
            Assert.IsFalse(h.IsAlive);
        }

        [Test]
        public void ShowGameObject_CollectsChildren()
        {
            var root = new GameObject("root");
            _objects.Add(root);
            CreateRenderer().transform.SetParent(root.transform);
            CreateRenderer().transform.SetParent(root.transform);
            var h = OutlineApi.Show(root, _style);
            _handles.Add(h);
            Assert.AreEqual(2, OutlineApi.GetRendererCount(h.Slot));
        }
    }
}
