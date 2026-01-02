using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class BridgeController : MonoBehaviour
{
    private VisualElement _root;
    private Button _scanButton;
    private Button _commButton;
    private Button _logButton;

    private void OnEnable()
    {
        // Get the root visual element from the UIDocument component
        _root = GetComponent<UIDocument>().rootVisualElement;

        // Find the buttons by name
        _scanButton = _root.Q<Button>("Scan");
        _commButton = _root.Q<Button>("Comm");
        _logButton = _root.Q<Button>("Log");

        // Register clicked callbacks
        if (_scanButton != null)
        {
            _scanButton.clicked += OnScanClicked;
        }

        if (_commButton != null)
        {
            _commButton.clicked += OnCommClicked;
        }

        if (_logButton != null)
        {
            _logButton.clicked += OnLogClicked;
        }
    }

    private void OnDisable()
    {
        // Unregister callbacks to avoid memory leaks
        if (_scanButton != null)
        {
            _scanButton.clicked -= OnScanClicked;
        }

        if (_commButton != null)
        {
            _commButton.clicked -= OnCommClicked;
        }

        if (_logButton != null)
        {
            _logButton.clicked -= OnLogClicked;
        }
    }

    private void OnScanClicked()
    {
        Debug.Log("System: [Scan]");
    }

    private void OnCommClicked()
    {
        Debug.Log("System: [Comm]");
    }

    private void OnLogClicked()
    {
        Debug.Log("System: [Log]");
    }
}
