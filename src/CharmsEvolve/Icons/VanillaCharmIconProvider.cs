using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CharmsEvolve.Icons
{
    /// <summary>
    /// Resolves the game's native inventory charm Sprite assets by name.
    /// IDs 41/42 are the copy-only Carefree Melody and Kingsoul identities;
    /// they fall back to the shared 40/36 slot art until an exact native asset
    /// or a CharmsEvolveApi.RegisterSprite override is available.
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
            for (int id = 1; id <= 42; id++)
            {
                Sprite best = null;
                int bestScore = int.MinValue;
                for (int i = 0; i < all.Length; i++)
                {
                    Sprite candidate = all[i];
                    int score = ScoreCandidate(candidate, id);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (best != null && bestScore > int.MinValue)
                    _sprites[id] = best;
            }

            Sprite fallback;
            if (!_sprites.ContainsKey(41) && _sprites.TryGetValue(40, out fallback))
            {
                _sprites[41] = fallback;
                Plugin.Log.LogWarning("Carefree Melody native Sprite was not resolved by name; using slot 40 art until RegisterSprite(\"X/Y/Z-41\", ...) is supplied.");
            }
            if (!_sprites.ContainsKey(42) && _sprites.TryGetValue(36, out fallback))
            {
                _sprites[42] = fallback;
                Plugin.Log.LogWarning("Kingsoul native Sprite was not resolved by name; using slot 36 art until RegisterSprite(\"X/Y/Z-42\", ...) is supplied.");
            }

            for (int id = 1; id <= 42; id++)
            {
                if (!_sprites.ContainsKey(id))
                    Plugin.Log.LogWarning("Native charm Sprite not found for charm " + id + ".");
            }
        }

        private static int ScoreCandidate(Sprite candidate, int id)
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.name))
                return int.MinValue;

            string name = candidate.name;
            int score = int.MinValue;
            if (id == 41)
            {
                if (ContainsAny(name, "carefree", "melody", "care_free", "carefree_melody"))
                    score = 500;
                else if (MatchesCharmNumber(name, 41))
                    score = 300;
            }
            else if (id == 42)
            {
                if (ContainsAny(name, "kingsoul", "king_soul", "king soul", "royalcharm", "royal_charm"))
                    score = 500;
                else if (MatchesCharmNumber(name, 42))
                    score = 300;
            }
            else if (MatchesCharmNumber(name, id))
            {
                score = 100;
            }

            if (score == int.MinValue)
                return score;
            if (name.StartsWith("Inv_", StringComparison.OrdinalIgnoreCase))
                score += 120;
            if (name.IndexOf("inventory", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 60;
            if (candidate.texture != null)
                score += 10;
            return score;
        }

        private static bool MatchesCharmNumber(string name, int id)
        {
            string pattern = @"(?:^|[_\s-])charm[_\s-]*0*" + id + @"(?:$|[_\s-])";
            return Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(name, @"(?:^|_)charm0*" + id + @"(?:$|_)", RegexOptions.IgnoreCase);
        }

        private static bool ContainsAny(string value, params string[] fragments)
        {
            for (int i = 0; i < fragments.Length; i++)
            {
                if (value.IndexOf(fragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
