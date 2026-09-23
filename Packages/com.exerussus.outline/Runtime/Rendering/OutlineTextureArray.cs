using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Exerussus.Outline.Rendering
{
    /// <summary>
    /// Текстуры стилей (паттерн, заливка) в одном массиве 256×256×16 с мипами: у разных записей разные
    /// текстуры, а композит — один проход. Слот текстуры держится, пока она нужна; копирование в массив —
    /// только для новых текстур (<see cref="FlushPending"/> из прохода Render Graph).
    /// Текстура приводится к квадрату 256×256.
    /// </summary>
    internal sealed class OutlineTextureArray : IDisposable
    {
        public const int Size = 256;
        public const int MaxSlices = 16;

        private readonly Texture[] _slices = new Texture[MaxSlices];
        private readonly bool[] _usedThisFrame = new bool[MaxSlices];
        private readonly Dictionary<Texture, int> _map = new();
        private readonly List<int> _pending = new(MaxSlices);
        private RenderTexture _array;
        private bool _warned;

        /// <summary>Массив для шейдера (null, пока не понадобилась ни одна текстура).</summary>
        public RenderTexture Array => _array;

        public bool HasPending => _pending.Count > 0;

        public void BeginFrame() => System.Array.Clear(_usedThisFrame, 0, MaxSlices);

        /// <summary>Слот текстуры в массиве; -1 — нет текстуры или массив полон.</summary>
        public int Request(Texture texture)
        {
            if (texture == null)
                return -1;
            if (_map.TryGetValue(texture, out int slot) && _slices[slot] == texture)
            {
                _usedThisFrame[slot] = true;
                return slot;
            }

            int free = -1;
            for (int i = 0; i < MaxSlices; i++)
            {
                if (_slices[i] == null) { free = i; break; }
            }
            if (free < 0)
            {
                for (int i = 0; i < MaxSlices; i++)
                {
                    if (!_usedThisFrame[i]) { free = i; break; }
                }
            }
            if (free < 0)
            {
                if (!_warned)
                {
                    Debug.LogWarning($"[Outline] Больше {MaxSlices} разных текстур стилей в кадре — лишние не рисуются.");
                    _warned = true;
                }
                return -1;
            }

            if (_slices[free] != null)
                _map.Remove(_slices[free]);
            _slices[free] = texture;
            _map[texture] = free;
            _usedThisFrame[free] = true;
            if (!_pending.Contains(free))
                _pending.Add(free);
            EnsureArray();
            return free;
        }

        /// <summary>Скопировать новые текстуры в массив и пересобрать мипы.</summary>
        public void FlushPending(CommandBuffer cmd)
        {
            if (_pending.Count == 0 || _array == null)
                return;
            for (int i = 0; i < _pending.Count; i++)
            {
                int slot = _pending[i];
                var src = _slices[slot];
                if (src != null)
                    cmd.Blit(src, _array, Vector2.one, Vector2.zero, 0, slot);
            }
            _pending.Clear();
            cmd.GenerateMips(_array);
        }

        private void EnsureArray()
        {
            if (_array != null)
                return;
            var desc = new RenderTextureDescriptor(Size, Size, GraphicsFormat.R8G8B8A8_SRGB, 0)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = MaxSlices,
                useMipMap = true,
                autoGenerateMips = false,
                msaaSamples = 1,
            };
            _array = new RenderTexture(desc)
            {
                name = "OutlineStyleTextures",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _array.Create();
        }

        public void Dispose()
        {
            if (_array != null)
            {
                _array.Release();
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(_array);
                else
                    UnityEngine.Object.DestroyImmediate(_array);
                _array = null;
            }
            _map.Clear();
            System.Array.Clear(_slices, 0, MaxSlices);
            _pending.Clear();
        }
    }
}
