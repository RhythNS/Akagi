using System;
using System.Collections.Generic;
using UnityEngine;

namespace Virtupad
{
    public static class ComponentObjectUtil
    {
        public static void SetGameObjectActive(Component[] components, bool active)
            => Array.ForEach(components, x => x.gameObject.SetActive(active));

        public static void SetGameObjectActive(GameObject[] components, bool active)
            => Array.ForEach(components, x => x.SetActive(active));

        public static void SetComponentActive(Behaviour[] components, bool active)
            => Array.ForEach(components, x => x.enabled = active);

        public static List<string> GetRecursiveListOfParents(Transform trans)
        {
            List<string> names = new List<string>();

            trans = trans.parent;
            while (trans != null)
            {
                names.Add(trans.name);
                trans = trans.parent;
            }

            return names;
        }
    }
}
