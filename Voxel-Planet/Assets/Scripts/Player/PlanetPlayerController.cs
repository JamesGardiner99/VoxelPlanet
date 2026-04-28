using UnityEngine;

namespace VoxelPlanet
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlanetPlayerController : MonoBehaviour
    {
        [Header("Ground Movement")]
        public float walkSpeed = 6f;
        public float jumpForce = 8f;
        public float gravityStrength = 20f;
        public float acceleration = 25f;

        [Header("Flying / Creative Movement")]
        public bool isFlying = false;
        public float flySpeed = 12f;
        public float flyAcceleration = 40f;
        public float doubleTapTime = 0.3f;

        [Header("Ground Check")]
        public float groundCheckDistance = 0.2f;
        public LayerMask groundMask;

        [Header("Mouse Look")]
        public Transform cameraPivot;
        public float mouseSensitivity = 200f;
        public float maxLookAngle = 80f;

        [Header("Planet")]
        public bool usePlanetTransform = false;
        public Transform planetTransform;
        public Vector3 planetCenter = Vector3.zero;

        private Rigidbody rb;
        private CapsuleCollider capsule;

        private float xRotation;
        private float yawInput;
        private float moveX;
        private float moveZ;
        private bool jumpRequested;
        private float lastSpaceTapTime = -1f;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();

            rb.useGravity = false;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxDepenetrationVelocity = 20f;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            Vector3 gravityUp = GetGravityUp();

            HandleMouseLook(gravityUp);

            moveX = Input.GetAxisRaw("Horizontal");
            moveZ = Input.GetAxisRaw("Vertical");

            if (Input.GetButtonDown("Jump"))
            {
                if (Time.time - lastSpaceTapTime <= doubleTapTime)
                {
                    isFlying = !isFlying;
                    jumpRequested = false;
                    rb.velocity = Vector3.ProjectOnPlane(rb.velocity, gravityUp);
                }
                else
                {
                    jumpRequested = true;
                }

                lastSpaceTapTime = Time.time;
            }
        }

        private void FixedUpdate()
        {
            Vector3 gravityUp = GetGravityUp();
            Vector3 gravityDown = -gravityUp;

            AlignToPlanet(gravityUp);

            if (isFlying)
            {
                ApplyFlyingMovement(gravityUp);
            }
            else
            {
                ApplyGroundMovement(gravityUp);
                ApplyGravity(gravityDown);
                HandleJump(gravityUp, gravityDown);
            }
        }

        private Vector3 GetPlanetCenter()
        {
            if (usePlanetTransform && planetTransform != null)
                return planetTransform.position;

            return planetCenter;
        }

        private Vector3 GetGravityUp()
        {
            Vector3 center = GetPlanetCenter();
            Vector3 direction = transform.position - center;

            if (direction.sqrMagnitude < 0.001f)
                return transform.up;

            return direction.normalized;
        }

        private void AlignToPlanet(Vector3 gravityUp)
        {
            Quaternion alignRotation =
                Quaternion.FromToRotation(transform.up, gravityUp) * rb.rotation;

            Quaternion yawRotation =
                Quaternion.AngleAxis(yawInput, gravityUp);

            Quaternion targetRotation =
                yawRotation * alignRotation;

            rb.MoveRotation(Quaternion.Slerp(
                rb.rotation,
                targetRotation,
                15f * Time.fixedDeltaTime
            ));

            yawInput = 0f;
        }

        private void HandleMouseLook(Vector3 gravityUp)
        {
            if (cameraPivot == null)
                return;

            float mouseX =
                Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;

            float mouseY =
                Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

            yawInput += mouseX;

            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);

            cameraPivot.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        }

        private void ApplyGroundMovement(Vector3 gravityUp)
        {
            Vector3 inputDirection =
                transform.right * moveX +
                transform.forward * moveZ;

            inputDirection =
                Vector3.ProjectOnPlane(inputDirection, gravityUp).normalized;

            Vector3 currentVelocity = rb.velocity;

            Vector3 verticalVelocity =
                Vector3.Project(currentVelocity, gravityUp);

            Vector3 horizontalVelocity =
                Vector3.ProjectOnPlane(currentVelocity, gravityUp);

            Vector3 targetHorizontalVelocity =
                inputDirection * walkSpeed;

            Vector3 newHorizontalVelocity = Vector3.MoveTowards(
                horizontalVelocity,
                targetHorizontalVelocity,
                acceleration * Time.fixedDeltaTime
            );

            rb.velocity = newHorizontalVelocity + verticalVelocity;
        }

        private void ApplyGravity(Vector3 gravityDown)
        {
            rb.AddForce(
                gravityDown * gravityStrength,
                ForceMode.Acceleration
            );
        }

        private void HandleJump(Vector3 gravityUp, Vector3 gravityDown)
        {
            if (!jumpRequested)
                return;

            jumpRequested = false;

            if (!IsGrounded(gravityDown))
                return;

            Vector3 velocity = rb.velocity;
            velocity = Vector3.ProjectOnPlane(velocity, gravityUp);
            velocity += gravityUp * jumpForce;

            rb.velocity = velocity;
        }

        private bool IsGrounded(Vector3 gravityDown)
        {
            Vector3 spherePosition =
                transform.position +
                gravityDown * ((capsule.height * 0.5f) - capsule.radius + groundCheckDistance);

            return Physics.CheckSphere(
                spherePosition,
                capsule.radius * 0.9f,
                groundMask,
                QueryTriggerInteraction.Ignore
            );
        }

        private void ApplyFlyingMovement(Vector3 gravityUp)
        {
            Vector3 inputDirection =
                transform.right * moveX +
                transform.forward * moveZ;

            if (Input.GetButton("Jump"))
                inputDirection += gravityUp;

            if (Input.GetKey(KeyCode.LeftControl))
                inputDirection -= gravityUp;

            inputDirection = inputDirection.normalized;

            Vector3 targetVelocity =
                inputDirection * flySpeed;

            rb.velocity = Vector3.MoveTowards(
                rb.velocity,
                targetVelocity,
                flyAcceleration * Time.fixedDeltaTime
            );
        }
    }
}