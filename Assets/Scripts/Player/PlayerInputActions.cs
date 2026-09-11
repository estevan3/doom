using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputActions : MonoBehaviour
{
    public static PlayerInputActions Instance;

    public InputActionAsset asset;
    public InputActionMap playerMap;

    public InputAction moveAction;
    public InputAction lookAction;
    public InputAction fireAction;
    public InputAction jumpAction;
    public InputAction sprintAction;
    public InputAction weapon1Action;
    public InputAction weapon2Action;
    public InputAction weapon3Action;
    public InputAction weapon4Action;
    public InputAction reloadAction;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SetupInputActions();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void SetupInputActions()
    {
        asset = ScriptableObject.CreateInstance<InputActionAsset>();

        playerMap = new InputActionMap("Player");

        moveAction = playerMap.AddAction("Move", InputActionType.Value);
        lookAction = playerMap.AddAction("Look", InputActionType.Value);
        fireAction = playerMap.AddAction("Fire", InputActionType.Button);
        jumpAction = playerMap.AddAction("Jump", InputActionType.Button);
        sprintAction = playerMap.AddAction("Sprint", InputActionType.Button);
        weapon1Action = playerMap.AddAction("Weapon1", InputActionType.Button);
        weapon2Action = playerMap.AddAction("Weapon2", InputActionType.Button);
        weapon3Action = playerMap.AddAction("Weapon3", InputActionType.Button);
        weapon4Action = playerMap.AddAction("Weapon4", InputActionType.Button);
        reloadAction = playerMap.AddAction("Reload", InputActionType.Button);

        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        lookAction.AddBinding("<Mouse>/delta");
        fireAction.AddBinding("<Mouse>/leftButton");
        jumpAction.AddBinding("<Keyboard>/space");
        sprintAction.AddBinding("<Keyboard>/leftShift");
        weapon1Action.AddBinding("<Keyboard>/1");
        weapon2Action.AddBinding("<Keyboard>/2");
        weapon3Action.AddBinding("<Keyboard>/3");
        weapon4Action.AddBinding("<Keyboard>/4");
        reloadAction.AddBinding("<Keyboard>/r");

        asset.AddActionMap(playerMap);
        asset.Enable();
    }

    void OnEnable()
    {
        if (asset != null) asset.Enable();
    }

    void OnDisable()
    {
        if (asset != null) asset.Disable();
    }
}
