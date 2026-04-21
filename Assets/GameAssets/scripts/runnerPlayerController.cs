using UnityEngine;
using System.Collections;

public class NewMonoBehaviourScript : MonoBehaviour
{
    [Header("Movement Settings")]
    public float forwardSpeed = 8f;
    public float laneDistance = 2f;
    public float horizontalSmooth = 10f;
    public float jumpForce = 6f;
    public float gravity = -22f;
    public float slideDuration = 0.68f;

    [Header("Reference")]
    public Animator animator;
    public CharacterController controller;
    public Transform sphereObject; // Assign the Sphere in Inspector
    public Transform characterRoot; // The character's main body/root (optional - will use this.transform if null)

    private int desiredLane = 1; // 0 = left, 1 = middle, 2 = right
    private float verticalVelocity = 0f;
    private bool isSliding = false;
    private float lastCharacterY = 0f;
    
    // Touch & Mouse Controls
    private Vector2 touchStartPos;
    private float touchStartTime;
    private bool isSwiping = false;
    private const float swipeThreshold = 50f; // Minimum swipe distance
    private const float swipeTimeThreshold = 0.5f; // Maximum time for a swipe

    void Start()
    {
        // Initialize character Y position tracking
        lastCharacterY = transform.position.y;
        
        // Attach sphere to character root so it jumps with the character
        if (sphereObject != null)
        {
            // Enable the sphere if it's disabled
            sphereObject.gameObject.SetActive(true);
            
            Transform root = characterRoot != null ? characterRoot : this.transform;
            sphereObject.parent = root;
            sphereObject.localPosition = new Vector3(0, 1.6f, 0); // Adjusted to face level
            sphereObject.localRotation = Quaternion.Euler(0, 90, 0); // 90 degrees on Y axis
                        // Make sphere transparent at start
            Renderer sphereRenderer = sphereObject.GetComponent<Renderer>();
            if (sphereRenderer != null)
            {
                Color color = sphereRenderer.material.color;
                color.a = 0f; // Set alpha to 0 (transparent)
                sphereRenderer.material.color = color;
            }
                        // Remove any rigidbody from sphere so it doesn't collide independently
            Rigidbody sphereRb = sphereObject.GetComponent<Rigidbody>();
            if (sphereRb != null)
            {
                sphereRb.isKinematic = true;
            }
            
            Debug.Log($"Sphere attached to character root and will jump with the character");
        }
    }

    void Update()
    {
        playerMovment();
        UpdateSpherePosition();
        PcControls();
    }
    
    void UpdateSpherePosition()
    {
        // Sync sphere Y position with character's actual Y position
        if (sphereObject != null)
        {
            // Calculate the Y offset from character's movement
            float currentCharacterY = transform.position.y;
            float yOffset = currentCharacterY - lastCharacterY;
            lastCharacterY = currentCharacterY;
            
            // Apply the offset to sphere's world Y position
            Vector3 sphereWorldPos = sphereObject.position;
            sphereWorldPos.y += yOffset;
            sphereObject.position = sphereWorldPos;
            
            // Adjust sphere Y position based on sliding
            Vector3 sphereLocalPos = sphereObject.localPosition;
            if (isSliding)
            {
                // Move sphere down to center when sliding
                sphereLocalPos.y = Mathf.Lerp(sphereLocalPos.y, 0.5f, Time.deltaTime * 10f);
            }
            else
            {
                // Move sphere up to head level when not sliding
                sphereLocalPos.y = Mathf.Lerp(sphereLocalPos.y, 1.0f, Time.deltaTime * 10f);
            }
            sphereObject.localPosition = sphereLocalPos;
        }
    }

    public void playerMovment()
    {
                // Horizontal Movement
        Vector3 move = Vector3.forward * forwardSpeed;

        float targetX = (desiredLane - 1) * laneDistance;
        float newX = Mathf.Lerp(transform.position.x, targetX, horizontalSmooth * Time.deltaTime);
        move.x = (newX - transform.position.x) / Time.deltaTime;

        // Vertical Movement
        if(controller.isGrounded && verticalVelocity < 0)
        {
            verticalVelocity = -1f; // Small value to keep the player grounded
            animator.SetBool("IsJumping", false);
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        move.y = verticalVelocity;

        // Move the player
        controller.Move(move * Time.deltaTime);
    }

    public void PcControls()
    {
        HandleTouchAndMouseInput();
        HandleKeyboardInput();
    }
    
    void HandleTouchAndMouseInput()
    {
        // Handle touch input (mobile)
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            
            if (touch.phase == TouchPhase.Began)
            {
                touchStartPos = touch.position;
                touchStartTime = Time.time;
                isSwiping = false;
            }
            else if (touch.phase == TouchPhase.Moved)
            {
                float swipeDistance = Vector2.Distance(touch.position, touchStartPos);
                if (swipeDistance > swipeThreshold)
                {
                    isSwiping = true;
                    float swipeDirection = touch.position.x - touchStartPos.x;
                    
                    // Swipe left
                    if (swipeDirection < -swipeThreshold)
                    {
                        desiredLane = Mathf.Max(desiredLane - 1, 0);
                        touchStartPos = touch.position; // Reset for continuous swiping
                    }
                    // Swipe right
                    else if (swipeDirection > swipeThreshold)
                    {
                        desiredLane = Mathf.Min(desiredLane + 1, 2);
                        touchStartPos = touch.position; // Reset for continuous swiping
                    }
                }
            }
            else if (touch.phase == TouchPhase.Ended)
            {
                float tapTime = Time.time - touchStartTime;
                float tapDistance = Vector2.Distance(touch.position, touchStartPos);
                
                // Tap (not a swipe)
                if (tapDistance < swipeThreshold && tapTime < swipeTimeThreshold)
                {
                    Jump(); // Tap = Jump
                }
                // Long press = Slide
                else if (tapTime > swipeTimeThreshold && !isSwiping)
                {
                    Slide();
                }
            }
        }
        
        // Handle mouse input (for testing in editor / mouse controls)
        // Only process mouse if not using touch
        if (Input.touchCount == 0)
        {
            if (Input.GetMouseButtonDown(0)) // Left click = Jump
            {
                Jump();
            }
            
            if (Input.GetMouseButtonDown(1)) // Right click = Slide
            {
                Slide();
                Debug.Log("Right click - Slide triggered");
            }
        }
    }
    
    void HandleKeyboardInput()
    {
        #if UNITY_EDITOR || UNITY_STANDALONE
        if(Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
        {
            desiredLane = Mathf.Min(desiredLane + 1, 2);
        }

        if(Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
        {
            desiredLane = Mathf.Max(desiredLane - 1, 0);
        }

        if(Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.Space))
        {
            Jump();
        }

        if(Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
        {
            Slide();
        }
        
        #endif
    }
    public void Jump()
    {
        if(!controller.isGrounded) return;
        
        verticalVelocity = jumpForce;
        animator.SetBool("IsJumping", true);
    }

    public void Slide()
    {
        if(!isSliding && controller.isGrounded)
        {
            StartCoroutine(DoSlide());
        }
    }

    private IEnumerator DoSlide()
    {
        isSliding = true;
        animator.SetBool("IsSliding", true);

        float origH = controller.height;
        Vector3 origC = controller.center;

        controller.height = origH / 2f;
        controller.center = new Vector3(origC.x, origC.y / 2f, origC.z);

        yield return new WaitForSeconds(slideDuration);

        controller.height = origH;
        controller.center = origC;

        animator.SetBool("IsSliding", false);
        isSliding = false;
    }
}
