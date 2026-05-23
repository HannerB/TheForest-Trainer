using BepInEx;
using BepInEx.Logging;
using TheForest.Utils;
using UnityEngine;

namespace TheForestTrainer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.hannerb.theforest.trainer";
        public const string PluginName = "TheForestTrainer";
        public const string PluginVersion = "0.2.0";

        internal static ManualLogSource Log;

        private bool _panelVisible;
        private bool _godMode;
        private bool _infiniteStamina;
        private bool _infiniteEnergy;
        private bool _noHunger;
        private bool _noThirst;
        private bool _noCold;

        private Rect _windowRect = new Rect(20, 20, 300, 0);

        private void Awake()
        {
            Log = Logger;
            Log.LogMessage(PluginName + " v" + PluginVersion + " cargado. F1 para abrir el panel.");
        }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F1))
            {
                _panelVisible = !_panelVisible;
            }

            PlayerStats stats = LocalPlayer.Stats;
            if (stats == null) return;

            if (_godMode)
            {
                stats.Health = 100f;
                stats.HealthTarget = 100f;
            }
            if (_infiniteStamina) stats.Stamina = 100f;
            if (_infiniteEnergy) stats.Energy = 100f;
            if (_noHunger) stats.Fullness = 1f;
            if (_noThirst) stats.Thirst = 0f;
            if (_noCold) stats.ColdArmor = 1f;
        }

        private void OnGUI()
        {
            if (!_panelVisible) return;
            _windowRect = GUILayout.Window(0xF0E5, _windowRect, DrawWindow,
                "TheForestTrainer v" + PluginVersion + "  (F1 para cerrar)");
        }

        private void DrawWindow(int id)
        {
            PlayerStats stats = LocalPlayer.Stats;

            if (stats == null)
            {
                GUILayout.Label("Esperando jugador.");
                GUILayout.Label("Entra a una partida para ver stats.");
                GUI.DragWindow();
                return;
            }

            GUILayout.Label("--- Lectura en vivo ---");
            GUILayout.Label("Health:   " + stats.Health.ToString("F0") + " / 100");
            GUILayout.Label("Stamina:  " + stats.Stamina.ToString("F0") + " / 100");
            GUILayout.Label("Energy:   " + stats.Energy.ToString("F0") + " / 100");
            GUILayout.Label("Fullness: " + stats.Fullness.ToString("F2") + " / 1.00");
            GUILayout.Label("Thirst:   " + stats.Thirst.ToString("F2") + " / 1.00");

            GUILayout.Space(8);
            GUILayout.Label("--- Cheats activos ---");

            _godMode          = GUILayout.Toggle(_godMode,          " God Mode (vida infinita)");
            _infiniteStamina  = GUILayout.Toggle(_infiniteStamina,  " Stamina infinita");
            _infiniteEnergy   = GUILayout.Toggle(_infiniteEnergy,   " Energy infinita");
            _noHunger         = GUILayout.Toggle(_noHunger,         " Sin hambre");
            _noThirst         = GUILayout.Toggle(_noThirst,         " Sin sed");
            _noCold           = GUILayout.Toggle(_noCold,           " Sin frio");

            GUILayout.Space(8);

            if (GUILayout.Button("Recargar todo una vez"))
            {
                stats.Health = 100f;
                stats.HealthTarget = 100f;
                stats.Stamina = 100f;
                stats.Energy = 100f;
                stats.Fullness = 1f;
                stats.Thirst = 0f;
                Log.LogInfo("Stats recargados manualmente");
            }

            GUI.DragWindow();
        }
    }
}
