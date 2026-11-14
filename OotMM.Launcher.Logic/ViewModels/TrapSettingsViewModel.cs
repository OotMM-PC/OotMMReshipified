using System.Globalization;

using OotMM.Core;

namespace OotMM.Launcher.ViewModels;

public sealed class TrapSettingsViewModel : ViewModelBase
{
    private readonly Action<TrapTuning> _changed;
    private TrapTuning _traps;
    private string _knockbackText = "";
    private string _knockbackError = "";

    public TrapSettingsViewModel(TrapTuning traps, Action<TrapTuning> changed)
    {
        _traps = (traps ?? new TrapTuning()).Normalized();
        _knockbackText = _traps.Knockback;
        _changed = changed;
    }

    internal TrapTuning Current => _traps;

    public bool IsOriginal => _traps.IsOriginal;

    public string Summary => _traps.IsOriginal ? "Traps: original" : "Traps: custom";

    public int IceBreakoutPercent
    {
        get => _traps.IceBreakoutPercent;
        set => EditTraps(t => t.IceBreakoutPercent = value);
    }

    public int IceDamagePercent
    {
        get => _traps.IceDamagePercent;
        set => EditTraps(t => t.IceDamagePercent = value);
    }

    public int FireDamagePercent
    {
        get => _traps.FireDamagePercent;
        set => EditTraps(t => t.FireDamagePercent = value);
    }

    public double FireSeconds
    {
        get => _traps.FireSeconds;
        set => EditTraps(t => t.FireSeconds = Math.Round(value * 2) / 2);
    }

    public int ShockDamagePercent
    {
        get => _traps.ShockDamagePercent;
        set => EditTraps(t => t.ShockDamagePercent = value);
    }

    public double ShockSeconds
    {
        get => _traps.ShockSeconds;
        set => EditTraps(t => t.ShockSeconds = Math.Round(value * 2) / 2);
    }

    public string IceBreakoutText => PercentText(_traps.IceBreakoutPercent, 100);

    public string IceDamageText => PercentText(_traps.IceDamagePercent, TrapTuning.OriginalDamagePercent);

    public string FireDamageText => PercentText(_traps.FireDamagePercent, TrapTuning.OriginalDamagePercent);

    public string FireSecondsText => SecondsText(_traps.FireSeconds, 10);

    public string ShockDamageText => PercentText(_traps.ShockDamagePercent, TrapTuning.OriginalDamagePercent);

    public string ShockSecondsText => SecondsText(_traps.ShockSeconds, 1);

    public string KnockbackText
    {
        get => _knockbackText;
        set
        {
            var text = (value ?? "").Trim();
            if (!TrapTuning.TryParseKnockback(text, out _, out var error))
            {
                Set(ref _knockbackText, text);
                Set(ref _knockbackError, error, nameof(KnockbackError));
                return;
            }

            Set(ref _knockbackText, text);
            Set(ref _knockbackError, "", nameof(KnockbackError));
            EditTraps(t => t.Knockback = text);
        }
    }

    public string KnockbackError => _knockbackError;

    public void Reset()
    {
        _traps = new TrapTuning().Normalized();
        _knockbackText = _traps.Knockback;
        _knockbackError = "";
        RaiseTraps();
        _changed(_traps);
    }

    public void Commit() => _changed(_traps);

    private void EditTraps(Action<TrapTuning> edit)
    {
        var next = new TrapTuning
        {
            IceBreakoutPercent = _traps.IceBreakoutPercent,
            IceDamagePercent = _traps.IceDamagePercent,
            FireDamagePercent = _traps.FireDamagePercent,
            FireSeconds = _traps.FireSeconds,
            ShockDamagePercent = _traps.ShockDamagePercent,
            ShockSeconds = _traps.ShockSeconds,
            Knockback = _traps.Knockback,
        };
        edit(next);
        next = next.Normalized();
        if (next.IceBreakoutPercent == _traps.IceBreakoutPercent && next.IceDamagePercent == _traps.IceDamagePercent
            && next.FireDamagePercent == _traps.FireDamagePercent && next.FireSeconds.Equals(_traps.FireSeconds)
            && next.ShockDamagePercent == _traps.ShockDamagePercent && next.ShockSeconds.Equals(_traps.ShockSeconds)
            && next.Knockback == _traps.Knockback)
        {
            return;
        }

        var knockbackChanged = next.Knockback != _traps.Knockback;
        _traps = next;
        RaiseTraps();
        if (knockbackChanged)
        {
            _changed(_traps);
        }
    }

    private void RaiseTraps()
    {
        foreach (var name in new[]
                 {
                     nameof(IceBreakoutPercent), nameof(IceDamagePercent), nameof(FireDamagePercent), nameof(FireSeconds),
                     nameof(ShockDamagePercent), nameof(ShockSeconds), nameof(IceBreakoutText), nameof(IceDamageText),
                     nameof(FireDamageText), nameof(FireSecondsText), nameof(ShockDamageText), nameof(ShockSecondsText),
                     nameof(KnockbackText), nameof(KnockbackError), nameof(IsOriginal), nameof(Summary),
                 })
        {
            Raise(name);
        }
    }

    private static string PercentText(int percent, int original) =>
        percent.ToString(CultureInfo.InvariantCulture) + "%" + (percent == original ? " (original)" : "");

    private static string SecondsText(double seconds, double original) =>
        seconds.ToString("0.#", CultureInfo.InvariantCulture) + " s" + (seconds.Equals(original) ? " (original)" : "");
}
