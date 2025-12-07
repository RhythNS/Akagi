using UnityEngine;
using UnityEngine.UI;

namespace Virtupad
{
    public static class UIUtil
    {
        // ---- Gotten from ----
        // https://stackoverflow.com/questions/52353898/get-column-and-row-of-count-from-gridlayoutgroup-programmatically
        public static void GetColAndRowsOfGridLayoutGroup(GridLayoutGroup group, out int colNum, out int rowNum)
        {
            if (group.transform.childCount == 0)
            {
                colNum = 0;
                rowNum = 0;
                return;
            }

            colNum = 0;
            Vector3 firstElementPosition = group.transform.GetChild(0).localPosition;
            foreach (Transform t in group.transform)
            {
                if (Mathf.Approximately(firstElementPosition.y, t.localPosition.y) == false)
                    break;
                colNum++;
            }

            rowNum = 1;
            foreach (Transform t in group.transform)
            {
                if (Mathf.Approximately(firstElementPosition.y, t.localPosition.y) == false)
                {
                    rowNum++;
                    firstElementPosition = t.localPosition;
                }
            }
        }
    }
}
