using UnityEngine;

public class FocusMover : MonoBehaviour
{
    [SerializeField] private float offsetY = -5f;
    [SerializeField] private float moveSpeed = 2f;

    private Vector3 startPosition;
    private Vector3 focusPosition;
    private bool isFocused = false;

    private void Start()
    {
        startPosition = transform.position;
        focusPosition = startPosition + new Vector3(0f, offsetY, 0f);
    }

    private void Update()
    {
        Vector3 target = isFocused ? focusPosition : startPosition;

        transform.position = Vector3.Lerp(
            transform.position,
            target,
            Time.deltaTime * moveSpeed
        );
    }

    public void Focus()
    {
        isFocused = true;
    }

    public void Unfocus()
    {
        isFocused = false;
    }
}