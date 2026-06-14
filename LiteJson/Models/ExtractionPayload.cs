using System;
using System.Collections.Generic;

namespace LiteJson.Models
{
    // ==========================================
    // 2. PAYLOAD PRINCIPAL (O ESTADO / SNAPSHOT)
    // ==========================================
    public class ExtractionPayload
    {
        public string StepId { get; set; } = Guid.NewGuid().ToString();
        public int? StepIndex { get; set; }
        public string StepName { get; set; } = string.Empty;

        /// <summary>
        /// Data/Hora exata do Snapshot. Fundamental para o LiteAutomation 
        /// calcular o delta de tempo entre o Estado A e o Estado B.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public bool IsEvidenceOnly { get; set; }
        public bool IsActive { get; set; } = true;
        public bool PendingConfirmation { get; set; }

        /// <summary>
        /// Indica se o UIA/AX_Tree já terminou de ser processado pela Thread STA em background.
        /// </summary>
        public bool IsHydrated { get; set; } = false;

        public string TriggerType { get; set; } = string.Empty;

        // A trilha de tudo que o usuário fez desde o último print
        public List<InteractionBreadcrumb> InteractionTrail { get; set; } = new List<InteractionBreadcrumb>();

        /// <summary>
        /// O elemento central do snapshot: o alvo direto da ação do usuário.
        /// Carrega a gaveta tripla simétrica (UIA + AX_Tree + BiDi) e o
        /// <c>AssociatedStepId</c> injetado pelo orquestrador no momento do print.
        /// </summary>
        public TargetElementData TargetElementData { get; set; } = new TargetElementData();

        /// <summary>
        /// O coração da arquitetura State-Driven: A fotografia completa de todos os elementos da tela.
        /// </summary>
        public ObservedContext ObservedContext { get; set; } = new ObservedContext();
    }
}