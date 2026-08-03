using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CharmsEvolve.Icons
{
    internal sealed class CharmTextureRegistry : IDisposable, ICharmTextureProvider
    {
        private readonly Dictionary<string, IconHandle> _overrides =
            new Dictionary<string, IconHandle>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Sprite> _overrideSprites =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Texture2D> _ownedTextures = new List<Texture2D>();
        private readonly List<Sprite> _ownedSprites = new List<Sprite>();
        private readonly VanillaCharmIconProvider _vanilla = new VanillaCharmIconProvider();

        public bool TryGetIcon(string charmKey, int originalCharmId, out IconHandle icon)
        {
            if (charmKey != null && _overrides.TryGetValue(charmKey, out icon))
                return icon.IsValid;

            return _vanilla.TryGetIcon(charmKey, originalCharmId, out icon);
        }

        /// <summary>
        /// UI path used by the native charm pane. Native assets are returned as the exact
        /// Resources.FindObjectsOfTypeAll&lt;Sprite&gt; Sprite. Texture overrides are converted
        /// to Sprite only here, so the existing public texture override API remains compatible.
        /// </summary>
        public bool TryGetSprite(string charmKey, int originalCharmId, out Sprite sprite)
        {
            if (!string.IsNullOrEmpty(charmKey) && _overrides.ContainsKey(charmKey))
            {
                if (_overrideSprites.TryGetValue(charmKey, out sprite) && sprite != null)
                    return true;

                IconHandle handle = _overrides[charmKey];
                Texture2D texture = handle.Texture as Texture2D;
                if (texture == null || !handle.IsValid)
                {
                    sprite = null;
                    return false;
                }

                Rect rect = new Rect(
                    handle.Uv.x * texture.width,
                    handle.Uv.y * texture.height,
                    handle.Uv.width * texture.width,
                    handle.Uv.height * texture.height);

                Sprite native;
                float pixelsPerUnit = 100f;
                Vector2 pivot = new Vector2(0.5f, 0.5f);
                if (_vanilla.TryGetSprite(charmKey, originalCharmId, out native) && native != null)
                {
                    pixelsPerUnit = native.pixelsPerUnit;
                    if (native.rect.width > 0f && native.rect.height > 0f)
                        pivot = new Vector2(native.pivot.x / native.rect.width, native.pivot.y / native.rect.height);
                }

                sprite = Sprite.Create(texture, rect, pivot, pixelsPerUnit);
                sprite.name = "CharmsEvolve.OverrideSprite." + charmKey;
                sprite.hideFlags = HideFlags.HideAndDontSave;
                _overrideSprites[charmKey] = sprite;
                _ownedSprites.Add(sprite);
                return true;
            }

            return _vanilla.TryGetSprite(charmKey, originalCharmId, out sprite);
        }

        public void Register(string charmKey, Texture texture, Rect uv)
        {
            if (string.IsNullOrEmpty(charmKey))
                throw new ArgumentException("charmKey is required.", "charmKey");
            if (texture == null)
                throw new ArgumentNullException("texture");

            DestroyOverrideSprite(charmKey);
            _overrides[charmKey] = new IconHandle(texture, uv);
        }

        public bool RegisterPng(string charmKey, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                texture.name = "CharmsEvolve.Override." + charmKey;
                if (!TryLoadImage(texture, bytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    return false;
                }

                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                _ownedTextures.Add(texture);
                Register(charmKey, texture, new Rect(0f, 0f, 1f, 1f));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Failed to load charm texture: " + ex);
                return false;
            }
        }

        private static bool TryLoadImage(Texture2D texture, byte[] bytes)
        {
            Type imageConversionType = Type.GetType(
                "UnityEngine.ImageConversion, UnityEngine.ImageConversionModule",
                false);

            if (imageConversionType == null)
                return false;

            MethodInfo method = imageConversionType.GetMethod(
                "LoadImage",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) },
                null);

            object result;
            if (method != null)
            {
                result = method.Invoke(null, new object[] { texture, bytes, false });
                return result is bool && (bool)result;
            }

            method = imageConversionType.GetMethod(
                "LoadImage",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[] { typeof(Texture2D), typeof(byte[]) },
                null);

            if (method == null)
                return false;

            result = method.Invoke(null, new object[] { texture, bytes });
            return result is bool && (bool)result;
        }

        public bool Unregister(string charmKey)
        {
            if (charmKey == null)
                return false;
            DestroyOverrideSprite(charmKey);
            return _overrides.Remove(charmKey);
        }

        public void Dispose()
        {
            for (int i = 0; i < _ownedSprites.Count; i++)
            {
                if (_ownedSprites[i] != null)
                    UnityEngine.Object.Destroy(_ownedSprites[i]);
            }
            for (int i = 0; i < _ownedTextures.Count; i++)
            {
                if (_ownedTextures[i] != null)
                    UnityEngine.Object.Destroy(_ownedTextures[i]);
            }

            _ownedSprites.Clear();
            _ownedTextures.Clear();
            _overrideSprites.Clear();
            _overrides.Clear();
            _vanilla.Dispose();
        }

        private void DestroyOverrideSprite(string charmKey)
        {
            Sprite sprite;
            if (!_overrideSprites.TryGetValue(charmKey, out sprite))
                return;

            _overrideSprites.Remove(charmKey);
            _ownedSprites.Remove(sprite);
            if (sprite != null)
                UnityEngine.Object.Destroy(sprite);
        }
    }
}
