using UnityEngine;

namespace Janelia
{
    // Makes the associated `GameObject` have collision detection and response
    // (per the org.janelia.collision-handling package) for kinematic motion from
    // the Mouse Treadmill system (per org.janelia.mouse-treadmill).  Uses `Janelia.MouseTreadmillUpdater`
    // implying the following orientation:
    // The direction for forward motion is the positive local Z axis.
    // The up direction is the positive local Y axis.

    // Also binds the 'q' and Escape keys to quit the application.

    public class MouseTreadmillSubject : KinematicSubject
    {
        // Remember that default values here are overridden by the values saved in the Unity scene.
        // So the values here are used only when an object using this script is first created.
        // After that, changes must be made in the Unity editor's Inspector.
        [SerializeField] private bool allowMovement = true;
        [SerializeField] private bool allowRotation = false;
        [SerializeField] private bool reverseDirection = false;
        [SerializeField] private bool logTreadmill = true;
        [SerializeField] private float pitchScale = 0.144f; // Calibration scale for pitch (degree / pixel)
        [SerializeField] private float rollScale = 0.170f; // Calibration scale for roll (degree / pixel)
        [SerializeField] private float yawScale = 0.112f; // Calibaration scale for yaw (degree / pixel)
        [SerializeField] private float forwardMultiplier = 1f;
        [SerializeField] private float sideMultiplier = 1f;
        [SerializeField] private bool enableKeyboard = false;
        [SerializeField] private float keyboardSpeed = 2.0f;
        [SerializeField] private float keyboardRotationSpeed = 90.0f;

        public new void Start()
        {
            _typedUpdater = new MouseTreadmillUpdater();
            _typedUpdater.allowMovement = allowMovement;
            _typedUpdater.allowRotation = allowRotation;
            _typedUpdater.reverseDirection = reverseDirection;
            _typedUpdater.logTreadmill = logTreadmill;
            _typedUpdater.rollScale = rollScale;
            _typedUpdater.pitchScale = pitchScale;
            _typedUpdater.yawScale = yawScale;
            _typedUpdater.enableKeyboard = enableKeyboard;
            _typedUpdater.keyboardSpeed = keyboardSpeed;
            _typedUpdater.keyboardRotationSpeed = keyboardRotationSpeed;
            _typedUpdater.allowRotation = allowRotation;

            updater = _typedUpdater;

            // The `collisionRadius` and `collisionPlaneNormal` fields are optional,
            // and if set, they are passed to the `Janelia.KinematicCollisionHandler`
            // created in the base class.  Do not set the `collisionRadius` field here,
            // so the value from the Unity Editor's Insepctor will be used.  But do set
            // `collisionPlaneNormal` to a value that is consisten with the assumptions
            // of `org.janelia.jettrac`.
            collisionPlaneNormal = new Vector3(0, 1, 0);

            // Let the base class finish the initial set-up.
            base.Start();
        }

        public new void Update()
        {
            _typedUpdater.allowMovement = allowMovement;

            if (Input.GetKey("q") || Input.GetKey(KeyCode.Escape))
            {
                Quit();
            }

            // Let the base updater update this subject's position, with sliding collision handling.
            base.Update();
        }

        private void Quit()
        {
            #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
            #elif UNITY_STANDALONE
                Application.Quit();
            #endif
        }

        public void OnDisable()
        {
            _typedUpdater.OnDisable();
        }

        private MouseTreadmillUpdater _typedUpdater;
        private Rigidbody _rigidbody;
    }
}