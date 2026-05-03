using System.Collections.Generic;

namespace LiteJson.Core
{
    public static class LiteJsonLanguageManager
    {
        // O idioma global injetado pela Nave-Mãe durante a inicialização
        public static string CurrentLanguage = "pt-BR";

        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
        {
            ["pt-BR"] = new()
            {
                ["SettingsTitle"] = "LiteJson: Motor de Captura Semântica",
                ["SettingsDesc"] = "O LiteJson atua como um colhedor passivo de dados (Fat Payload).",
                ["EngineToggle"] = "Status do Motor:",
                ["EngineOn"] = "LIGADO (Ativo)",
                ["EngineOff"] = "DESLIGADO",
                ["TargetEngine"] = "Motor Alvo da Sessão",
                ["WebUniversal"] = "Web Universal (BiDi + UIA + AX Tree)",
                ["SapEnterprise"] = "SAP Enterprise (SAP Scripting + UIA)",
                ["OutputPath"] = "Diretório de Saída (JSON e Imagens):",
                ["BrowseBtn"] = "Procurar...",
                ["SaveBtn"] = "Salvar Configurações",
                ["SavedStatus"] = "Configurações salvas e aplicadas com sucesso!"
            },
            ["en-US"] = new()
            {
                ["SettingsTitle"] = "LiteJson: Semantic Capture Engine",
                ["SettingsDesc"] = "LiteJson acts as a passive data harvester (Fat Payload).",
                ["EngineToggle"] = "Engine Status:",
                ["EngineOn"] = "ON (Active)",
                ["EngineOff"] = "OFF",
                ["TargetEngine"] = "Session Target Engine",
                ["WebUniversal"] = "Web Universal (BiDi + UIA + AX Tree)",
                ["SapEnterprise"] = "SAP Enterprise (SAP Scripting + UIA)",
                ["OutputPath"] = "Output Directory (JSON and Images):",
                ["BrowseBtn"] = "Browse...",
                ["SaveBtn"] = "Save Settings",
                ["SavedStatus"] = "Settings saved and applied successfully!"
            },
            ["es-ES"] = new()
            {
                ["SettingsTitle"] = "LiteJson: Motor de Captura Semántica",
                ["SettingsDesc"] = "LiteJson actúa como un recolector pasivo de datos (Fat Payload).",
                ["EngineToggle"] = "Estado del Motor:",
                ["EngineOn"] = "ENCENDIDO (Activo)",
                ["EngineOff"] = "APAGADO",
                ["TargetEngine"] = "Motor Objetivo de la Sesión",
                ["WebUniversal"] = "Web Universal (BiDi + UIA + AX Tree)",
                ["SapEnterprise"] = "SAP Enterprise (SAP Scripting + UIA)",
                ["OutputPath"] = "Directorio de Salida (JSON e Imágenes):",
                ["BrowseBtn"] = "Buscar...",
                ["SaveBtn"] = "Guardar Configuración",
                ["SavedStatus"] = "¡Configuraciones guardadas y aplicadas!"
            },
            ["fr-FR"] = new()
            {
                ["SettingsTitle"] = "LiteJson : Moteur de Capture Sémantique",
                ["SettingsDesc"] = "LiteJson agit comme un collecteur de données passif (Fat Payload).",
                ["EngineToggle"] = "Statut du Moteur :",
                ["EngineOn"] = "ALLUMÉ (Actif)",
                ["EngineOff"] = "ÉTEINT",
                ["TargetEngine"] = "Moteur Cible de la Session",
                ["WebUniversal"] = "Web Universel (BiDi + UIA + AX Tree)",
                ["SapEnterprise"] = "SAP Enterprise (SAP Scripting + UIA)",
                ["OutputPath"] = "Répertoire de Sortie (JSON et Images) :",
                ["BrowseBtn"] = "Parcourir...",
                ["SaveBtn"] = "Enregistrer les Paramètres",
                ["SavedStatus"] = "Paramètres enregistrés et appliqués avec succès !"
            },
            ["de-DE"] = new()
            {
                ["SettingsTitle"] = "LiteJson: Semantische Erfassungs-Engine",
                ["SettingsDesc"] = "LiteJson fungiert als passiver Datensammler (Fat Payload).",
                ["EngineToggle"] = "Motorstatus:",
                ["EngineOn"] = "EIN (Aktiv)",
                ["EngineOff"] = "AUS",
                ["TargetEngine"] = "Ziel-Engine der Sitzung",
                ["WebUniversal"] = "Web Universal (BiDi + UIA + AX Tree)",
                ["SapEnterprise"] = "SAP Enterprise (SAP Scripting + UIA)",
                ["OutputPath"] = "Ausgabeverzeichnis (JSON und Bilder):",
                ["BrowseBtn"] = "Durchsuchen...",
                ["SaveBtn"] = "Einstellungen Speichern",
                ["SavedStatus"] = "Einstellungen erfolgreich gespeichert und angewendet!"
            },
            ["it-IT"] = new()
            {
                ["SettingsTitle"] = "LiteJson: Motore di Acquisizione Semantica",
                ["SettingsDesc"] = "LiteJson agisce come un raccoglitore di dati passivo (Fat Payload).",
                ["EngineToggle"] = "Stato del Motore:",
                ["EngineOn"] = "ACCESO (Attivo)",
                ["EngineOff"] = "SPENTO",
                ["TargetEngine"] = "Motore Target della Sessione",
                ["WebUniversal"] = "Web Universale (BiDi + UIA + AX Tree)",
                ["SapEnterprise"] = "SAP Enterprise (SAP Scripting + UIA)",
                ["OutputPath"] = "Directory di Output (JSON e Immagini):",
                ["BrowseBtn"] = "Sfoglia...",
                ["SaveBtn"] = "Salva Impostazioni",
                ["SavedStatus"] = "Impostazioni salvate e applicate con successo!"
            }
        };

        public static string GetString(string key)
        {
            if (Translations.ContainsKey(CurrentLanguage) && Translations[CurrentLanguage].ContainsKey(key))
                return Translations[CurrentLanguage][key];
            return key; // Fallback
        }
    }
}