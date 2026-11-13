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

    public double IceDamageHearts
    {
        get => _traps.IceDamageQuarters / 4.0;
        set => EditTraps(t => t.IceDamageQuarters = (int)Math.Round(value * 4));
    }

    public double FireDamageHearts
    {
        get => _traps.FireDamageQuarters / 4.0;
        set => EditTraps(t => t.FireDamageQuarters = (int)Math.Round(value * 4));
    }

    public double FireSeconds
    {
        get => _traps.FireSeconds;
        set => EditTraps(t => t.FireSeconds = Math.Round(value * 2) / 2);
    }

    public double ShockDamageHearts
    {
        get => _traps.ShockDamageQuarters / 4.0;
        set => EditTraps(t => t.ShockDamageQuarters = (int)Math.Round(value * 4));
    }

    public double ShockSeconds
    {
        get => _traps.ShockSeconds;
        set => EditTraps(t => t.ShockSeconds = Math.Round(value * 2) / 2);
    }

    public string IceBreakoutText => $"{_traps.IceBreakoutPercent}%";

    public string IceDamageText => HeartsText(_traps.IceDamageQuarters);

    public string FireDamageText => HeartsText(_traps.FireDamageQuarters);

    public string FireSecondsText => SecondsText(_traps.FireSeconds);

    public string ShockDamageText => HeartsText(_traps.ShockDamageQuarters);

    public string ShockSecondsText => SecondsText(_traps.ShockSeconds);

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
        _traps = new TrapTuning();
        _knockbackText = "";
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
            IceDamageQuarters = _traps.IceDamageQuarters,
            FireDamageQuarters = _traps.FireDamageQuarters,
            FireSeconds = _traps.FireSeconds,
            ShockDamageQuarters = _traps.ShockDamageQuarters,
            ShockSeconds = _traps.ShockSeconds,
            Knockback = _traps.Knockback,
        };
        edit(next);
        next = next.Normalized();
        if (next.IceBreakoutPercent == _traps.IceBreakoutPercent && next.IceDamageQuarters == _traps.IceDamageQuarters
            && next.FireDamageQuarters == _traps.FireDamageQuarters && next.FireSeconds.Equals(_traps.FireSeconds)
            && next.ShockDamageQuarters == _traps.ShockDamageQuarters && next.ShockSeconds.Equals(_traps.ShockSeconds)
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
                     nameof(IceBreakoutPercent), nameof(IceDamageHearts), nameof(FireDamageHearts), nameof(FireSeconds),
                     nameof(ShockDamageHearts), nameof(ShockSeconds), nameof(IceBreakoutText), nameof(IceDamageText),
                     nameof(FireDamageText), nameof(FireSecondsText), nameof(ShockDamageText), nameof(ShockSecondsText),
                     nameof(KnockbackText), nameof(KnockbackError), nameof(IsOriginal), nameof(Summary),
                 })
        {
            Raise(name);
        }
    }

    private static string HeartsText(int quarters) =>
        quarters == 0 ? "none" : (quarters / 4.0).ToString("0.##", CultureInfo.InvariantCulture)
                                  + (quarters == 4 ? " heart" : " hearts");

    private static string SecondsText(double seconds) =>
        seconds.ToString("0.#", CultureInfo.InvariantCulture) + " s";
}
