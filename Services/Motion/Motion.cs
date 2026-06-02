namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Constantes de la Fase 3 (microinteracciones): duraciones, desfases y amplitudes en un
/// único sitio. Nada de literales sueltos de tiempo/offset repartidos por el render — todo
/// se referencia desde aquí (regla de oro F3: "sin literales de offset nuevos").
/// </summary>
public static class Motion
{
    /// <summary>Tween de números/barras (ease-out). ~220 ms.</summary>
    public const double TweenMs = 220.0;

    /// <summary>Fade de opacidad al abrir el panel y fade-in del hover. ~120 ms.</summary>
    public const double FadeMs = 120.0;

    /// <summary>Desfase por índice en la entrada escalonada de secciones. ~40 ms.</summary>
    public const double StaggerMs = 40.0;

    /// <summary>Duración del tramo de cada sección en la entrada escalonada. ~180 ms.</summary>
    public const double StaggerDurMs = 180.0;

    /// <summary>Desplazamiento vertical máximo (px) de la entrada escalonada.</summary>
    public const int StaggerMaxOffsetPx = 6;

    /// <summary>Amplitud (px) del bote de atención de la mascota.</summary>
    public const int BounceAmplitudePx = 6;

    /// <summary>Periodo (ms) de un rebote completo del bote de atención.</summary>
    public const double BouncePeriodMs = 420.0;

    /// <summary>Número de rebotes que decaen en un bote de atención.</summary>
    public const int BounceRepeats = 3;

    /// <summary>Intervalo del tick rápido cuando hay algo animando (~30 fps).</summary>
    public const int FastTickMs = 33;

    /// <summary>Intervalo del tick lento (solo countdown, panel visible sin animación).</summary>
    public const int SlowTickMs = 1000;
}
