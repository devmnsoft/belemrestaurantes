namespace BelemRestaurantes.Domain.Enums;

/// <summary>Ciclo de vida de um estabelecimento no catálogo.</summary>
public enum RestaurantStatus
{
    /// <summary>Descoberto pela automação; invisível no site até decisão humana.</summary>
    Pending = 0,

    /// <summary>Aprovado e visível publicamente.</summary>
    Published = 1,

    /// <summary>Recusado na moderação.</summary>
    Rejected = 2,

    /// <summary>Publicado anteriormente e removido do ar sem apagar o histórico.</summary>
    Suspended = 3
}

/// <summary>Confiança declarada sobre uma informação vinda de fonte externa.</summary>
public enum DataQuality
{
    /// <summary>Obtido da fonte, ainda não conferido por pessoa.</summary>
    Unverified = 0,

    /// <summary>Conferido contra outra fonte independente.</summary>
    Corroborated = 1,

    /// <summary>Confirmado por pessoa responsável na moderação.</summary>
    Verified = 2,

    /// <summary>Conflito detectado entre fontes; não deve ser exibido.</summary>
    Conflicting = 3
}

/// <summary>Estados de um job persistido na fila de descoberta.</summary>
public enum DiscoveryJobState
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,

    /// <summary>Desistiu após esgotar as tentativas; operador precisa intervir.</summary>
    DeadLettered = 4
}

/// <summary>Provedores de IA da camada Cuia IA.</summary>
public enum AiProvider
{
    Disabled = 0,
    Gemini = 1,
    Groq = 2,
    DeepSeek = 3,
    Drael = 4
}

/// <summary>Motivo pelo qual um item entrou na fila de moderação.</summary>
public enum ModerationReason
{
    NewDiscovery = 1,
    AiLowConfidence = 2,
    DataConflict = 3,
    MediaWithoutAuthorization = 4,
    DuplicateSuspected = 5,
    ReportByUser = 6
}