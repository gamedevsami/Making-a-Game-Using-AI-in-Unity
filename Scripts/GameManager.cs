using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UNITY 2D ENDLESS RUNNER — GameManager.cs
/// Handles: Player jump, obstacle/coin spawning (pooled),
/// ground looping, score, Game Over, and all animations.
/// </summary>
public class GameManager : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // SECTION 1 — PLAYER
    // ─────────────────────────────────────────────
    [Header("── Player ──")]
    [SerializeField] private Rigidbody2D playerRb;          // Player's Rigidbody2D
    [SerializeField] private Animator   playerAnimator;     // Player's Animator
    [SerializeField] private float      jumpForce   = 12f;  // How high the player jumps
    [SerializeField] private float      gravityScale = 3f;  // Feel of gravity (higher = snappier)
    [SerializeField] private LayerMask  groundLayer;        // Layer your ground tiles are on
    [SerializeField] private Transform  groundCheck;        // Empty child object under player feet
    [SerializeField] private float      groundCheckRadius = 0.15f;

    // ─────────────────────────────────────────────
    // SECTION 2 — GROUND LOOPING
    // ─────────────────────────────────────────────
    [Header("── Ground ──")]
    [SerializeField] private Transform[] groundTiles;       // Assign 2-3 ground tile GameObjects
    [SerializeField] private float groundTileWidth  = 20f;  // Width of one ground tile in world units
    [SerializeField] private float groundRecycleX   = -15f; // X position at which tile gets recycled

    // ─────────────────────────────────────────────
    // SECTION 3 — SPAWNING
    // ─────────────────────────────────────────────
    [Header("── Spawning ──")]
    [SerializeField] private GameObject[] obstaclePrefabs;  // 3 obstacle prefabs
    [SerializeField] private GameObject   coinPrefab;       // Coin prefab
    [SerializeField] private Transform    spawnPoint;       // Empty object placed off-screen right
    [SerializeField] private float spawnIntervalMin  = 1.8f;
    [SerializeField] private float spawnIntervalMax  = 3.5f;
    [SerializeField] private float coinSpawnChance   = 0.4f; // 40% chance a coin spawns instead

    // ─────────────────────────────────────────────
    // SECTION 4 — WORLD SPEED
    // ─────────────────────────────────────────────
    [Header("── World Speed ──")]
    [SerializeField] private float startSpeed    = 6f;
    [SerializeField] private float maxSpeed      = 18f;
    [SerializeField] private float speedIncrease = 0.3f;   // Speed added per second
    private float currentSpeed;

    // ─────────────────────────────────────────────
    // SECTION 5 — SCORE & UI
    // ─────────────────────────────────────────────
    [Header("── UI ──")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI finalScoreText;
    [SerializeField] private GameObject      gameOverPanel;
    [SerializeField] private int             coinBonus = 50;

    // ─────────────────────────────────────────────
    // SECTION 6 — VFX ANIMATIONS
    // ─────────────────────────────────────────────
    [Header("── VFX ──")]
    [SerializeField] private GameObject explosionPrefab;  // Explosion animation prefab
    [SerializeField] private GameObject dustPrefab;       // Dust puff animation prefab
    [SerializeField] private GameObject coinSparkPrefab;  // Coin sparkle animation prefab

    // ─────────────────────────────────────────────
    // PRIVATE STATE
    // ─────────────────────────────────────────────
    private bool  isGameOver    = false;
    private bool  isGrounded    = false;
    private float score         = 0f;
    private float spawnTimer    = 0f;
    private float nextSpawnTime = 2f;

    // Object Pool — avoids Instantiate/Destroy every frame
    private Queue<GameObject> obstaclePool = new Queue<GameObject>();
    private Queue<GameObject> coinPool     = new Queue<GameObject>();
    private const int POOL_SIZE = 8;

    // Obstacle move component reference cache
    private List<ObstacleMover> activeObstacles = new List<ObstacleMover>();
    private List<CoinMover>     activeCoins      = new List<CoinMover>();

    // ─────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────

    private void Awake()
    {
        // Apply gravity scale to player rigidbody
        if (playerRb != null)
            playerRb.gravityScale = gravityScale;

        // Pre-warm object pools to avoid hiccups at runtime
        PrewarmPool(obstaclePrefabs[0], obstaclePool, POOL_SIZE);
        PrewarmPool(coinPrefab,          coinPool,     POOL_SIZE);

        currentSpeed = startSpeed;
    }

    private void Start()
    {
        gameOverPanel.SetActive(false);
        UpdateScoreUI();
    }

    private void Update()
    {
        if (isGameOver) return;

        HandleInput();
        HandleGroundCheck();
        HandleSpawning();
        HandleScore();
        HandleSpeedRamp();
        LoopGroundTiles();
    }

    // ─────────────────────────────────────────────
    // INPUT — Tap anywhere to jump
    // ─────────────────────────────────────────────

    private void HandleInput()
    {
        // Works for touch (mobile) AND mouse click (editor testing)
        bool tapped = Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began
                   || Input.GetMouseButtonDown(0);

        if (tapped && isGrounded)
        {
            Jump();
        }
    }

    private void Jump()
    {
        // Zero out vertical velocity first (prevents double-jump stacking)
        playerRb.velocity = new Vector2(playerRb.velocity.x, 0f);
        playerRb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

        // Play dust VFX at player feet
        if (dustPrefab != null)
        {
            Vector3 dustPos = playerRb.transform.position + Vector3.down * 0.5f;
            GameObject dust = Instantiate(dustPrefab, dustPos, Quaternion.identity);
            Destroy(dust, 1.5f);
        }

        // Set jump animation bool
        playerAnimator?.SetBool("IsJumping", true);
    }

    // ─────────────────────────────────────────────
    // GROUND CHECK — circle overlap below feet
    // ─────────────────────────────────────────────

    private void HandleGroundCheck()
    {
        bool wasGrounded = isGrounded;
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        if (!wasGrounded && isGrounded)
        {
            // Just landed
            playerAnimator?.SetBool("IsJumping", false);
        }
    }

    // ─────────────────────────────────────────────
    // SPAWNING — obstacles and coins
    // ─────────────────────────────────────────────

    private void HandleSpawning()
    {
        spawnTimer += Time.deltaTime;
        if (spawnTimer < nextSpawnTime) return;

        spawnTimer = 0f;
        nextSpawnTime = Random.Range(spawnIntervalMin, spawnIntervalMax);

        // Decrease interval over time for more challenge
        spawnIntervalMin = Mathf.Max(0.8f, spawnIntervalMin - 0.01f);
        spawnIntervalMax = Mathf.Max(1.5f, spawnIntervalMax - 0.01f);

        if (Random.value < coinSpawnChance)
            SpawnCoin();
        else
            SpawnObstacle();
    }

    private void SpawnObstacle()
    {
        // Pick random obstacle type
        int index = Random.Range(0, obstaclePrefabs.Length);

        GameObject obj = GetFromPool(obstaclePool, obstaclePrefabs[index]);
        obj.transform.position = spawnPoint.position;
        obj.SetActive(true);

        // Add or get mover component
        ObstacleMover mover = obj.GetComponent<ObstacleMover>();
        if (mover == null) mover = obj.AddComponent<ObstacleMover>();
        mover.Init(currentSpeed, this);
        activeObstacles.Add(mover);
    }

    private void SpawnCoin()
    {
        // Spawn coin slightly above ground
        Vector3 pos = spawnPoint.position + Vector3.up * Random.Range(0.5f, 2.5f);
        GameObject obj = GetFromPool(coinPool, coinPrefab);
        obj.transform.position = pos;
        obj.SetActive(true);

        CoinMover mover = obj.GetComponent<CoinMover>();
        if (mover == null) mover = obj.AddComponent<CoinMover>();
        mover.Init(currentSpeed, this);
        activeCoins.Add(mover);
    }

    // ─────────────────────────────────────────────
    // GROUND TILE LOOPING
    // ─────────────────────────────────────────────

    private void LoopGroundTiles()
    {
        foreach (Transform tile in groundTiles)
        {
            // Move tile left
            tile.position += Vector3.left * currentSpeed * Time.deltaTime;

            // If tile is far enough left, snap it to the right of the last tile
            if (tile.position.x < groundRecycleX)
            {
                // Find the rightmost tile
                float rightmostX = GetRightmostTileX();
                tile.position = new Vector3(rightmostX + groundTileWidth, tile.position.y, tile.position.z);
            }
        }
    }

    private float GetRightmostTileX()
    {
        float max = float.MinValue;
        foreach (Transform t in groundTiles)
            if (t.position.x > max) max = t.position.x;
        return max;
    }

    // ─────────────────────────────────────────────
    // SCORE
    // ─────────────────────────────────────────────

    private void HandleScore()
    {
        // Score grows with speed — faster run = more points per second
        score += Time.deltaTime * currentSpeed * 0.5f;
        UpdateScoreUI();
    }

    private void UpdateScoreUI()
    {
        if (scoreText != null)
            scoreText.text = "Score: " + Mathf.FloorToInt(score).ToString();
    }

    // Called by CoinMover when player collects coin
    public void AddCoinScore()
    {
        score += coinBonus;

        if (coinSparkPrefab != null)
        {
            // VFX is spawned by CoinMover at coin position — see CoinMover below
        }

        UpdateScoreUI();
    }

    // ─────────────────────────────────────────────
    // SPEED RAMP
    // ─────────────────────────────────────────────

    private void HandleSpeedRamp()
    {
        currentSpeed = Mathf.Min(currentSpeed + speedIncrease * Time.deltaTime, maxSpeed);

        // Also update speed for all active movers
        foreach (var m in activeObstacles) if (m != null) m.speed = currentSpeed;
        foreach (var m in activeCoins)     if (m != null) m.speed = currentSpeed;
    }

    // ─────────────────────────────────────────────
    // GAME OVER
    // ─────────────────────────────────────────────

    // Called by ObstacleMover when it detects collision with player
    public void TriggerGameOver(Vector3 explosionPos)
    {
        if (isGameOver) return;
        isGameOver = true;

        // Stop player running animation
        playerAnimator?.SetBool("IsRunning", false);
        playerAnimator?.SetBool("IsJumping", false);

        // Disable player physics so it stops moving
        playerRb.velocity = Vector2.zero;
        playerRb.bodyType = RigidbodyType2D.Static;

        // Play explosion VFX
        if (explosionPrefab != null)
        {
            GameObject expl = Instantiate(explosionPrefab, explosionPos, Quaternion.identity);
            Destroy(expl, 2f);
        }

        // Hide player sprite (explosion replaces it)
        playerRb.GetComponent<SpriteRenderer>().enabled = false;

        // Show Game Over UI after short delay
        StartCoroutine(ShowGameOverAfterDelay(1.2f));
    }

    private IEnumerator ShowGameOverAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (finalScoreText != null)
            finalScoreText.text = "Score: " + Mathf.FloorToInt(score).ToString();

        gameOverPanel.SetActive(true);
    }

    // ─────────────────────────────────────────────
    // RESTART (wire to Restart Button onClick)
    // ─────────────────────────────────────────────

    public void RestartGame()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    // ─────────────────────────────────────────────
    // OBJECT POOL HELPERS
    // ─────────────────────────────────────────────

    private void PrewarmPool(GameObject prefab, Queue<GameObject> pool, int count)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject obj = Instantiate(prefab);
            obj.SetActive(false);
            pool.Enqueue(obj);
        }
    }

    private GameObject GetFromPool(Queue<GameObject> pool, GameObject prefab)
    {
        // Grab an inactive object, or create a new one if pool is empty
        while (pool.Count > 0)
        {
            GameObject obj = pool.Dequeue();
            if (obj != null)
            {
                obj.SetActive(false);
                return obj;
            }
        }
        return Instantiate(prefab);
    }

    // Called by movers when their object goes off-screen
    public void ReturnToPool(GameObject obj, Queue<GameObject> pool)
    {
        obj.SetActive(false);
        pool.Enqueue(obj);
    }

    public Queue<GameObject> GetObstaclePool() => obstaclePool;
    public Queue<GameObject> GetCoinPool()     => coinPool;
}


// ═══════════════════════════════════════════════════
// ObstacleMover — attached automatically to obstacles
// ═══════════════════════════════════════════════════
public class ObstacleMover : MonoBehaviour
{
    public float speed;
    private GameManager gm;
    private bool hitPlayer = false;

    public void Init(float spd, GameManager manager)
    {
        speed    = spd;
        gm       = manager;
        hitPlayer = false;
    }

    private void Update()
    {
        transform.position += Vector3.left * speed * Time.deltaTime;

        // Recycle when off-screen left
        if (transform.position.x < -20f)
        {
            gm.ReturnToPool(gameObject, gm.GetObstaclePool());
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hitPlayer) return;
        if (other.CompareTag("Player"))
        {
            hitPlayer = true;
            gm.TriggerGameOver(transform.position);
            gameObject.SetActive(false);
        }
    }
}


// ═══════════════════════════════════════════════════
// CoinMover — attached automatically to coins
// ═══════════════════════════════════════════════════
public class CoinMover : MonoBehaviour
{
    public float speed;
    private GameManager gm;
    [SerializeField] private GameObject coinSparkPrefab;

    public void Init(float spd, GameManager manager)
    {
        speed = spd;
        gm    = manager;
    }

    private void Update()
    {
        // Gentle bobbing motion makes coins feel lively
        transform.position += new Vector3(
            -speed * Time.deltaTime,
            Mathf.Sin(Time.time * 3f) * 0.015f,
            0f
        );

        if (transform.position.x < -20f)
        {
            gm.ReturnToPool(gameObject, gm.GetCoinPool());
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            // Spawn sparkle VFX
            if (coinSparkPrefab != null)
            {
                GameObject spark = Instantiate(coinSparkPrefab, transform.position, Quaternion.identity);
                Destroy(spark, 1f);
            }

            gm.AddCoinScore();
            gm.ReturnToPool(gameObject, gm.GetCoinPool());
        }
    }
}