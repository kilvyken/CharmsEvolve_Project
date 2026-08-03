using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CharmsEvolve.Icons
{
    /// <summary>
    /// Resolves the game's native inventory charm Sprite assets by name.
    /// No tk2d atlas scraping and no unrelated Texture fallback is used.
    /// </summary>
    internal sealed class VanillaCharmIconProvider : IDisposable, ICharmTextureProvider
    {
        private readonly Dictionary<int, Sprite> _sprites = new Dictionary<int, Sprite>();
        private bool _scanned;

        public bool TryGetSprite(string charmKey, int originalCharmId, out Sprite sprite)
        {
            EnsureScanned();
            return _sprites.TryGetValue(originalCharmId, out sprite) && sprite != null;
        }

        public bool TryGetIcon(string charmKey, int originalCharmId, out IconHandle icon)
        {
            Sprite sprite;
            if (!TryGetSprite(charmKey, originalCharmId, out sprite))
            {
                icon = default(IconHandle);
                return false;
            }

            Texture texture = sprite.texture;
            Rect textureRect = sprite.textureRect;
            Rect uv = new Rect(
                textureRect.x / texture.width,
                textureRect.y / texture.height,
                textureRect.width / texture.width,
                textureRect.height / texture.height);
            icon = new IconHandle(texture, uv);
            return true;
        }

        public void Dispose()
        {
            _sprites.Clear();
            _scanned = false;
        }

        private void EnsureScanned()
        {
            if (_scanned)
                return;
            _scanned = true;

            Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
            for (int id = 1; id <= 40; id++)
            {
                Sprite best = null;
                int bestScore = int.MinValue;
                Regex suffix = new Regex(@"(?:^|_)charm0*" + id + @"(?:$|_)", RegexOptions.IgnoreCase);

                for (int i = 0; i < all.Length; i++)
                {
                    Sprite candidate = all[i];
                    if (candidate == null || string.IsNullOrEmpty(candidate.name))
                        continue;
                    if (!suffix.IsMatch(candidate.name))
                        continue;

                    int score = 0;
                    if (candidate.name.StartsWith("Inv_", StringComparison.OrdinalIgnoreCase))
                        score += 100;
                    if (candidate.name.EndsWith("_charm" + id, StringComparison.OrdinalIgnoreCase))
                        score += 80;
                    if (candidate.name.IndexOf("charm" + id + "_", StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 20;
                    if (candidate.texture != null)
                        score += 10;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (best != null)
                    _sprites[id] = best;
                else
                    Plugin.Log.LogWarning("Native charm Sprite not found for charm " + id + ".");
            }
        }
    }
}
