using System;
using System.Collections.Generic;

namespace CharmsEvolve.Save
{
    [Serializable]
    internal sealed class CharmSaveData
    {
        public int Version = 1;
        public List<string> Owned = new List<string>();
        public List<string> Equipped = new List<string>();
    }
}
