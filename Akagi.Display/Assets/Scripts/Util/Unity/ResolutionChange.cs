using UnityEngine;

namespace Virtupad
{
    public delegate void ResolutionChanged(Vector2Int newResolution);

    public class ResolutionChange : MonoBehaviour
    {
        public static ResolutionChange Instance { get; private set; }
     
        public event ResolutionChanged OnResolutionChanged;

        private void OnRectTransformDimensionsChange()
        {
            Cursor.visible = false;
            OnResolutionChanged?.Invoke(new Vector2Int(Screen.width, Screen.height));
        }

        private void Awake()
        {
            if (Instance)
            {
                Debug.LogWarning("ResolutionChange already in scene. Deleting myself!");
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
