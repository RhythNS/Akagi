using UnityEngine;

public static class RaycastUtil
{
    public static T GetClosest<T>(Vector3 ownPosition, Vector3 position, Vector3 direction, float maxRange, int bitmask, out Vector3 impactPoint)
    {
        RaycastHit[] raycastHits = Physics.RaycastAll(position, direction, maxRange, bitmask);

        T closest = default;
        float closestLength = float.MaxValue;
        Vector3 ownPos = ownPosition;
        impactPoint = Vector3.zero;
        for (int i = 0; i < raycastHits.Length; i++)
        {
            if (raycastHits[i].collider.TryGetComponent(out T interactable) == false)
                continue;

            float lengthAway = (ownPos - raycastHits[i].point).sqrMagnitude;

            if (closest != null && lengthAway > closestLength)
                continue;

            closest = interactable;
            closestLength = lengthAway;
            impactPoint = raycastHits[i].point;
        }

        return closest;
    }

}
