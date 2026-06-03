namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Contenedor de <see cref="AnimatedValue"/> indexados por clave (<c>"bar:5h"</c>, <c>"bar:7d"</c>,
/// <c>"num:crit"</c>, <c>"pace"</c>…) que el render <b>muestrea en tiempo de pintado</b>. Es la capa
/// de estado entre <c>DashboardForm</c> (que llama <see cref="SetTarget"/> al llegar un snapshot) y
/// el dibujo (que lee <see cref="Display"/>).
///
/// <para>PURO y elapsed-driven (regla de oro F3): <see cref="Advance"/> recibe el delta de tiempo por
/// parámetro; jamás lee el reloj ni <c>Math.Random</c>. Así los tests son deterministas sin GDI+/reloj.</para>
///
/// <para>Semántica clave:</para>
/// <list type="bullet">
/// <item><see cref="Display"/> <b>siembra</b> una clave nueva directamente en su objetivo (no hay tween
/// "desde 0" la primera vez que aparece un dato). Con <c>reduceMotion</c> devuelve el target al instante
/// — es la puerta única del gate de reduce-motion (Tarea 7).</item>
/// <item><see cref="SetTarget"/>/<see cref="Set"/> rearman la animación hacia el nuevo objetivo desde el
/// valor actual (sin salto). Con <c>reduceMotion</c> colapsan al instante (snap).</item>
/// <item><see cref="Advance"/> integra el tiempo en todas las claves; <see cref="IsAnimating"/> es cierto
/// mientras alguna esté en vuelo (alimenta al <see cref="MotionScheduler"/>).</item>
/// </list>
/// </summary>
public sealed class MotionState
{
    private readonly Dictionary<string, AnimatedValue> _values = new();

    /// <summary>Devuelve (creándolo si hace falta) el <see cref="AnimatedValue"/> sembrado en <paramref name="seed"/>.</summary>
    private AnimatedValue GetOrSeed(string key, double seed)
    {
        if (!_values.TryGetValue(key, out var v))
        {
            v = new AnimatedValue(seed);
            _values[key] = v;
        }
        return v;
    }

    /// <summary>
    /// Valor a pintar para <paramref name="key"/>. Si la clave es nueva, se siembra en
    /// <paramref name="target"/> (sin tween de primera aparición). Con <paramref name="reduceMotion"/>
    /// devuelve el target al instante; si no, muestrea la curva animada en curso.
    /// </summary>
    public double Display(string key, double target, bool reduceMotion)
    {
        var v = GetOrSeed(key, target);
        return reduceMotion ? target : v.Value;
    }

    /// <summary>
    /// Rearma la clave hacia <paramref name="target"/> con <paramref name="durMs"/>/<paramref name="ease"/>
    /// explícitos. Con <paramref name="reduceMotion"/> colapsa al objetivo (snap) por la puerta única.
    /// </summary>
    public void Set(string key, double target, double durMs, Func<double, double>? ease = null, bool reduceMotion = false)
    {
        var v = GetOrSeed(key, target);
        if (reduceMotion)
        {
            // Gate único de reduce-motion: el objetivo se asienta sin animación.
            v.Set(target, 0);
            v.Snap();
            return;
        }
        v.Set(target, durMs, ease);
    }

    /// <summary>
    /// Atajo del tween estándar (números/barras): <see cref="Motion.TweenMs"/> + <see cref="Easing.OutCubic"/>.
    /// Con <paramref name="reduceMotion"/> colapsa al objetivo al instante.
    /// </summary>
    public void SetTarget(string key, double target, bool reduceMotion)
        => Set(key, target, Motion.TweenMs, Easing.OutCubic, reduceMotion);

    /// <summary>Integra <paramref name="elapsedMs"/> en todas las claves activas.</summary>
    public void Advance(double elapsedMs)
    {
        if (elapsedMs <= 0) return;
        foreach (var v in _values.Values) v.Advance(elapsedMs);
    }

    /// <summary>Lleva todas las claves a su objetivo al instante (gate global de reduce-motion).</summary>
    public void SnapAll()
    {
        foreach (var v in _values.Values) v.Snap();
    }

    /// <summary><c>true</c> mientras alguna clave esté animando (alimenta al scheduler).</summary>
    public bool IsAnimating
    {
        get
        {
            foreach (var v in _values.Values)
                if (v.IsAnimating) return true;
            return false;
        }
    }
}
