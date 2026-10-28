using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class ForestWoodChipView : MonoBehaviour
{
    [Header("기존 시스템")]
    [SerializeField] private MusicPlayback musicPlayback;
    [SerializeField] private RhythmInputTester rhythmInputTester;

    [Header("좌우 타격 위치")]
    [SerializeField] private RectTransform leftImpactPoint;
    [SerializeField] private RectTransform rightImpactPoint;

    [Header("파편 원본")]
    [SerializeField] private Image chipTemplate;

    [SerializeField, Range(64, 1024)]
    private int poolSize = 384;

    [Header("판정별 파편 수")]
    [SerializeField, Min(0)] private int normalCount = 24;
    [SerializeField, Min(0)] private int goodCount = 40;
    [SerializeField, Min(0)] private int perfectCount = 64;

    [SerializeField, Range(1.0f, 3.0f)]
    private float holdCountMultiplier = 1.5f;

    [Header("움직임")]
    [SerializeField, Min(0.1f)]
    private float speedMultiplier = 1.0f;

    [SerializeField, Min(0.0f)]
    private float gravity = 850.0f;

    private struct Chip
    {
        public Image Image;
        public RectTransform Rect;
        public Vector2 Origin;
        public Vector2 Velocity;
        public Color StartColor;
        public float Rotation;
        public float Spin;
        public float Lifetime;
        public double StartedAt;
        public bool Active;
    }

    private RectTransform root;
    private Chip[] chips;
    private Color templateColor;

    private int nextIndex;
    private int observedPlaybackId = -1;

    // 연출용 난수는 노트 생성에 사용하는 난수와 분리한다.
    private readonly System.Random random =
        new System.Random(5281);

    private void Awake()
    {
        root = (RectTransform)transform;

        if (musicPlayback == null ||
            rhythmInputTester == null ||
            leftImpactPoint == null ||
            rightImpactPoint == null ||
            chipTemplate == null ||
            chipTemplate.transform == transform ||
            !chipTemplate.transform.IsChildOf(transform))
        {
            Debug.LogError(
                "음악, 입력기, 좌우 타격 위치와 " +
                "WoodChips 아래의 파편 원본을 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        templateColor = chipTemplate.color;
        chipTemplate.gameObject.SetActive(false);

        chips = new Chip[Mathf.Clamp(poolSize, 64, 1024)];

        for (int i = 0; i < chips.Length; i++)
        {
            Image image = Instantiate(chipTemplate, root, false);

            image.name = $"WoodChip_{i + 1}";
            image.raycastTarget = false;
            image.enabled = true;
            image.gameObject.SetActive(false);

            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax =
                new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;

            chips[i].Image = image;
            chips[i].Rect = rect;
        }
    }

    private void OnEnable()
    {
        if (chips == null) { return; }

        observedPlaybackId = musicPlayback.PlaybackId;
        rhythmInputTester.NoteJudged += HandleJudgment;
    }

    private void OnDisable()
    {
        if (rhythmInputTester != null)
        {
            rhythmInputTester.NoteJudged -= HandleJudgment;
        }

        ClearChips();
    }

    private void HandleJudgment(
        RhythmNoteData note,
        RhythmJudgment judgment,
        double songTime)
    {
        if (chips == null ||
            !isActiveAndEnabled ||
            !musicPlayback.IsGameplayPlayback ||
            musicPlayback.HasNaturallyEnded ||
            musicPlayback.IsPaused ||
            !rhythmInputTester.isActiveAndEnabled ||
            note == null ||
            !note.IsResource ||
            judgment == RhythmJudgment.Miss)
        {
            return;
        }

        ResetForNewPlayback();

        int count;

        switch (judgment)
        {
            case RhythmJudgment.Perfect:
                count = perfectCount;
                break;

            case RhythmJudgment.Good:
                count = goodCount;
                break;

            default:
                count = normalCount;
                break;
        }

        float strength = 1.0f;

        if (note.IsHold)
        {
            count = Mathf.RoundToInt(
                count * Mathf.Clamp(holdCountMultiplier, 1.0f, 3.0f));

            strength = 1.25f;
        }

        bool both = note.Side == RhythmInputSide.Both;

        // Sync의 좌우 파편이 같은 판정 안에서 서로의 슬롯을 덮어쓰지 않게 한다.
        count = Mathf.Clamp(
            count, 0, chips.Length / (both ? 2 : 1));

        if (note.Side == RhythmInputSide.Left || both)
        {
            Emit(leftImpactPoint, -1.0f, count, strength, songTime);
        }

        if (note.Side == RhythmInputSide.Right || both)
        {
            Emit(rightImpactPoint, 1.0f, count, strength, songTime);
        }
    }

    private void Emit(
        RectTransform point,
        float direction,
        int count,
        float strength,
        double songTime)
    {
        if (point == null) { return; }

        Vector2 origin = root.InverseTransformPoint(point.position);

        float speed =
            Mathf.Max(0.1f, speedMultiplier) * strength;

        for (int i = 0; i < count; i++)
        {
            ref Chip chip = ref chips[nextIndex];
            nextIndex = (nextIndex + 1) % chips.Length;

            chip.Origin = origin + new Vector2(
                Roll(-6.0f, 6.0f),
                Roll(-6.0f, 6.0f));

            chip.Velocity = new Vector2(
                direction * Roll(90.0f, 280.0f),
                Roll(100.0f, 340.0f)) * speed;

            chip.Rotation = Roll(0.0f, 360.0f);
            chip.Spin = Roll(-720.0f, 720.0f);
            chip.Lifetime = Roll(0.25f, 0.55f);
            chip.StartedAt = songTime;
            chip.Active = true;

            // 작은 파편 사이에 큰 파편을 섞는다.
            float size = i % 6 == 0 ? 1.7f : 1.0f;

            chip.Rect.sizeDelta = new Vector2(
                Roll(3.0f, 7.0f),
                Roll(6.0f, 14.0f)) * size;

            chip.StartColor = Color.Lerp(
                templateColor, Color.white, Roll(0.0f, 0.25f));

            chip.StartColor.a = templateColor.a;

            chip.Rect.localPosition = new Vector3(
                chip.Origin.x, chip.Origin.y, 0.0f);

            chip.Rect.localRotation =
                Quaternion.Euler(0.0f, 0.0f, chip.Rotation);

            chip.Image.color = chip.StartColor;
            chip.Image.gameObject.SetActive(true);
        }
    }

    private void LateUpdate()
    {
        if (chips == null) { return; }

        if (musicPlayback == null || rhythmInputTester == null)
        {
            ClearChips();
            enabled = false;
            return;
        }

        ResetForNewPlayback();

        if (!musicPlayback.IsGameplayPlayback ||
            musicPlayback.HasNaturallyEnded ||
            !rhythmInputTester.isActiveAndEnabled)
        {
            ClearChips();
            return;
        }

        if (musicPlayback.IsPaused) { return; }

        if (!musicPlayback.TryGetSongTime(out double songTime))
        {
            ClearChips();
            return;
        }

        float acceleration = Mathf.Max(0.0f, gravity);

        for (int i = 0; i < chips.Length; i++)
        {
            ref Chip chip = ref chips[i];

            if (!chip.Active) { continue; }

            float elapsed = Mathf.Max(
                0.0f, (float)(songTime - chip.StartedAt));

            float progress = elapsed / chip.Lifetime;

            if (progress >= 1.0f)
            {
                HideChip(ref chip);
                continue;
            }

            Vector2 position =
                chip.Origin + chip.Velocity * elapsed;

            position.y -=
                0.5f * acceleration * elapsed * elapsed;

            chip.Rect.localPosition =
                new Vector3(position.x, position.y, 0.0f);

            chip.Rect.localRotation = Quaternion.Euler(
                0.0f, 0.0f,
                chip.Rotation + chip.Spin * elapsed);

            Color color = chip.StartColor;

            color.a *= 1.0f -
                Mathf.SmoothStep(
                    0.0f, 1.0f,
                    Mathf.InverseLerp(0.55f, 1.0f, progress));

            chip.Image.color = color;
        }
    }

    private float Roll(float minimum, float maximum)
    {
        return Mathf.Lerp(
            minimum, maximum, (float)random.NextDouble());
    }

    private void ResetForNewPlayback()
    {
        if (observedPlaybackId == musicPlayback.PlaybackId)
        {
            return;
        }

        ClearChips();
        observedPlaybackId = musicPlayback.PlaybackId;
    }

    private void HideChip(ref Chip chip)
    {
        chip.Active = false;

        if (chip.Image != null)
        {
            chip.Image.gameObject.SetActive(false);
        }
    }

    private void ClearChips()
    {
        if (chips == null) { return; }

        for (int i = 0; i < chips.Length; i++)
        {
            if (chips[i].Active)
            {
                HideChip(ref chips[i]);
            }
        }

        nextIndex = 0;
    }

    private void OnDestroy()
    {
        if (chips == null) { return; }

        foreach (Chip chip in chips)
        {
            if (chip.Image != null)
            {
                Destroy(chip.Image.gameObject);
            }
        }
    }
}