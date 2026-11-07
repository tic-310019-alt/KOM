using UnityEngine;

namespace KOM
{
    public static class KomVisual
    {
        // Grados extra si el modelo no mira a +Z. Se ajusta aqui y aplica a todo el juego.
        public const float ModelForwardYaw = 0f;

        public static Quaternion FaceDirection(float dirX, float cameraTurn = 0f)
        {
            float baseYaw = dirX >= 0f ? 90f : -90f;
            if (dirX < 0f) cameraTurn = -cameraTurn;
            return Quaternion.Euler(0f, baseYaw + cameraTurn + ModelForwardYaw, 0f);
        }

        public static Quaternion PreviewRotation(float dirX)
        {
            return FaceDirection(dirX, 18f);
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        public static void StripColliders(GameObject go)
        {
            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
                KomFx.SafeDestroy(collider);
        }
    }
}
