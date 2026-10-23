using System;
using Lumina.Core.Audio;
using Lumina.Core.Data;
using Lumina.Core.Progress;
using Lumina.Runtime.Audio;
using Lumina.Runtime.Battle;
using Lumina.Runtime.UI;
using UnityEngine;

namespace Lumina.Runtime
{
    /// <summary>Resources/LuminaData/*.json 을 읽는 데이터 공급원(빌드에 포함됨).</summary>
    public sealed class ResourcesDataSource : IDataSource
    {
        public string ReadText(string name)
        {
            var ta = Resources.Load<TextAsset>("LuminaData/" + name);
            return ta != null ? ta.text : null;
        }
    }

    /// <summary>
    /// 앱 루트: 데이터 로드·검증 → 저장 로드 → 화면 흐름(로비 → 스테이지 → 편성 → 전투 → 결과).
    /// 씬 설정 없이 Play 만 눌러도 LuminaBootstrap 이 자동 생성한다.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        public static GameApp Instance { get; private set; }

        public GameDatabase Db { get; private set; }
        public GameSession Session { get; private set; }
        public SfxPlayer Sfx { get; private set; }
        public Camera MainCamera { get; private set; }
        public bool DevTools { get; private set; }

        private Canvas _canvas;
        private LobbyScreen _lobby;
        private StageSelectScreen _stages;
        private PartySetupScreen _party;
        private ResultScreen _result;
        private Toast _toast;
        private ConfirmDialog _confirm;
        private BattleController _battle;
        private RectTransform _battleUi;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            DevTools = Application.isEditor || Debug.isDebugBuild;

            MainCamera = Camera.main;
            if (MainCamera == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                MainCamera = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            MainCamera.transform.SetParent(transform, false);
            MainCamera.clearFlags = CameraClearFlags.SolidColor;
            MainCamera.backgroundColor = Theme.Ink;

            var sfxGo = new GameObject("Sfx");
            sfxGo.transform.SetParent(transform, false);
            Sfx = sfxGo.AddComponent<SfxPlayer>();

            Ui.EnsureEventSystem();
            _canvas = Ui.CreateCanvas("UI", 10);
            _canvas.transform.SetParent(transform, false);

            Db = GameDatabase.Load(new ResourcesDataSource());
            var errors = Db.Validate();
            if (errors.Count > 0)
            {
                foreach (var e in errors) Debug.LogError("[Lumina] 데이터 오류: " + e);
                ShowFatal("데이터 검증 실패", string.Join("\n", errors));
                return;
            }

            try
            {
                Session = new GameSession(Db, new FileSaveStore(Application.persistentDataPath), DevTools);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ShowFatal("저장 데이터를 불러오지 못했습니다", ex.Message + "\n\n저장 위치: " + Application.persistentDataPath);
                return;
            }

            _lobby = new LobbyScreen(this, _canvas.transform);
            _stages = new StageSelectScreen(this, _canvas.transform);
            _party = new PartySetupScreen(this, _canvas.transform);
            _result = new ResultScreen(this, _canvas.transform);
            _battleUi = Ui.Rect(_canvas.transform, "BattleLayer");
            Ui.Place(_battleUi, 0, 0, 1, 1);
            _confirm = new ConfirmDialog(_canvas.transform);
            _toast = new Toast(_canvas.transform);
            Debug.Log($"[Lumina] 시작: 데이터 OK(무기 {Db.Weapons.Count}, 검증용 캐릭터 {Db.Characters.Count}, 스테이지 {Db.Stages.Count}), " +
                      $"렌더 파이프라인 {(Mats.IsUrp ? "URP" : "Built-in")}, 저장 {Application.persistentDataPath}");
            ShowLobby();
        }

        private void Update()
        {
            _toast?.Update(Time.unscaledDeltaTime);
        }

        private void HideAll()
        {
            _lobby?.Hide();
            _stages?.Hide();
            _party?.Hide();
            _result?.Hide();
        }

        public void ShowLobby()
        {
            EndBattleScene();
            HideAll();
            _lobby.Show();
        }

        public void ShowStageSelect()
        {
            EndBattleScene();
            HideAll();
            _stages.Show();
        }

        public void ShowParty(string stageId)
        {
            EndBattleScene();
            HideAll();
            _party.Open(stageId);
            _party.Show();
        }

        public void StartBattle(StartBattleResult start)
        {
            EndBattleScene();
            HideAll();
            _battle = BattleController.Begin(this, start, _battleUi);
        }

        /// <summary>전투 종료(보상은 이미 확정됨) → 결과 화면.</summary>
        public void OnBattleFinished(BattleSummary s)
        {
            _result.Set(s);
            EndBattleScene();
            HideAll();
            _stages.Select(s.stage.id);
            _result.Show();
        }

        private void EndBattleScene()
        {
            if (_battle != null)
            {
                Destroy(_battle.gameObject);
                _battle = null;
            }
            if (MainCamera != null)
            {
                MainCamera.transform.position = new Vector3(0f, 10f, -10f);
                MainCamera.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
                MainCamera.backgroundColor = Theme.Ink;
            }
        }

        public void Toast(string msg) => _toast?.Show(msg);

        public void Confirm(string title, string body, Action ok) => _confirm?.Show(title, body, ok);

        public void PlayUiClick() => Sfx?.PlaySystem(SystemSfx.UiClick, 0.6f);

        private void ShowFatal(string title, string body)
        {
            var bg = Ui.Panel(_canvas.transform, "Fatal", new Color(0.12f, 0.05f, 0.07f), true);
            var t = Ui.Label(bg.transform, $"<b>{title}</b>\n\n{body}", 28, Theme.Text, TextAnchor.UpperLeft);
            Ui.Place(t.rectTransform, 0, 0, 1, 1, 60, 60, 60, 60);
        }
    }

    /// <summary>씬에 GameApp 이 없으면 자동 생성(클린 체크아웃에서 아무 씬이나 Play 해도 실행).</summary>
    public static class LuminaBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (UnityEngine.Object.FindFirstObjectByType<GameApp>() != null) return;
            new GameObject("GameApp").AddComponent<GameApp>();
        }
    }
}
