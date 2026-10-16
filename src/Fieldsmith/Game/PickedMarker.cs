using UnityEngine;

namespace Fieldsmith
{
    // Sits on a placed pickable's m_hideWhenPicked object. Vanilla shows and hides that object
    // (Pickable.Awake, SetPicked and SetEnabled call SetActive on it), and Unity then calls OnEnable or OnDisable
    // here, so the marker is shown exactly while the real model is hidden, on every client with Fieldsmith,
    // without patching the pick.
    internal sealed class PickedMarker : MonoBehaviour
    {
        internal GameObject Marker;

        private void OnEnable()
        {
            if (Marker) Marker.SetActive(false);
        }

        private void OnDisable()
        {
            if (Marker) Marker.SetActive(true);
        }
    }
}
