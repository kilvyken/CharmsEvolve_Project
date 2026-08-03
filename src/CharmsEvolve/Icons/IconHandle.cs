using UnityEngine;

namespace CharmsEvolve.Icons
{
    public struct IconHandle
    {
        public Texture Texture;
        public Rect Uv;

        public IconHandle(Texture texture, Rect uv)
        {
            Texture = texture;
            Uv = uv;
        }

        public bool IsValid
        {
            get { return Texture != null && Uv.width > 0f && Uv.height > 0f; }
        }

        public static IconHandle Full(Texture texture)
        {
            return new IconHandle(texture, new Rect(0f, 0f, 1f, 1f));
        }
    }
}
