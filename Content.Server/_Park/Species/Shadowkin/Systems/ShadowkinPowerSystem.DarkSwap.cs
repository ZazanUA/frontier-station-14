using System.Linq;
using Content.Server.Magic;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Server._Park.Species.Shadowkin.Components;
using Content.Server._Park.Species.Shadowkin.Events;
using Content.Shared.Eye;
// using Content.Shader.Visible;
using Content.Shared.Actions;
using Content.Shared.Actions.ActionTypes;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage.Systems;
using Content.Shared._Park.Species.Shadowkin.Components;
using Content.Shared._Park.Species.Shadowkin.Events;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Content.Shared.Ghost;
using Robust.Shared.Prototypes;

namespace Content.Server._Park.Species.Shadowkin.Systems;

public sealed class ShadowkinDarkSwapSystem : EntitySystem
{
    [Dependency] private readonly ShadowkinPowerSystem _power = default!;
    [Dependency] private readonly VisibilitySystem _visibility = default!;
    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly ShadowkinDarkenSystem _darken = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly SharedStealthSystem _stealth = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    
    [Dependency] private readonly SharedEyeSystem _eye = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShadowkinDarkSwapPowerComponent, ComponentStartup>(Startup);
        SubscribeLocalEvent<ShadowkinDarkSwapPowerComponent, ComponentShutdown>(Shutdown);

        SubscribeLocalEvent<ShadowkinDarkSwapPowerComponent, ShadowkinDarkSwapEvent>(DarkSwap);

        SubscribeLocalEvent<ShadowkinDarkSwappedComponent, ComponentStartup>(OnInvisStartup);
        SubscribeLocalEvent<ShadowkinDarkSwappedComponent, ComponentShutdown>(OnInvisShutdown);
    }


    private void Startup(EntityUid uid, ShadowkinDarkSwapPowerComponent component, ComponentStartup args)
    {    
        _actions.AddAction(uid, ref component.DarkSwapActionEntity, component.DarkSwapAction);

        // _actions.AddAction(uid, new InstantAction(_prototype.Index<InstantActionPrototype>("ShadowkinDarkSwap")), null);

        // _actions.AddAction(uid, ref component.RestActionEntity, component.RestAction);
    }

    private void Shutdown(EntityUid uid, ShadowkinDarkSwapPowerComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.DarkSwapActionEntity);
        // _actions.RemoveAction(uid, new InstantAction(_prototype.Index<InstantActionPrototype>("ShadowkinDarkSwap")));
    }


    private void DarkSwap(EntityUid uid, ShadowkinDarkSwapPowerComponent component, ShadowkinDarkSwapEvent args)
    {
        // Need power to drain power
        if (!_entity.HasComponent<ShadowkinComponent>(args.Performer))
            return;

        // Don't activate abilities if handcuffed
        // TODO: Something like the Psionic Headcage to disable powers for Shadowkin
        if (_entity.TryGetComponent<HandcuffComponent>(args.Performer, out var cuffs) && cuffs.AntiShadowkin)
            return;


        var hasComp = _entity.HasComponent<ShadowkinDarkSwappedComponent>(args.Performer);

        SetDarkened(
            args.Performer,
            !_entity.HasComponent<ShadowkinDarkSwappedComponent>(args.Performer),
            args.SoundOn,
            args.VolumeOn,
            args.SoundOff,
            args.VolumeOff,
            args,
            args.StaminaCostOn,
            args.PowerCostOn,
            args.StaminaCostOff,
            args.PowerCostOff

        );

        _magic.Speak(args);
    }


    public void SetDarkened(
        EntityUid performer,
        bool addComp,
        SoundSpecifier? soundOn,
        float? volumeOn,
        SoundSpecifier? soundOff,
        float? volumeOff,
        ShadowkinDarkSwapEvent? args,
        float staminaCostOn = 0,
        float powerCostOn = 0,
        float staminaCostOff = 0,
        float powerCostOff = 0
    )
    {
        var ev = new ShadowkinDarkSwapAttemptEvent(performer);
        RaiseLocalEvent(ev);
        if (ev.Cancelled)
            return;

        if (!_entity.TryGetComponent<ShadowkinDarkSwapPowerComponent>(performer, out var power))
            return;

        if (addComp)
        {
            var comp = _entity.EnsureComponent<ShadowkinDarkSwappedComponent>(performer);
            comp.Invisible = power.Invisible;
            comp.Pacify = power.Pacify;
            comp.Darken = power.Darken;
            comp.DarkenRange = power.DarkenRange;
            comp.DarkenRate = power.DarkenRate;

            // Tell other systems we've DarkSwapped
            RaiseNetworkEvent(new ShadowkinDarkSwappedEvent(_entity.GetNetEntity(performer), true));

            // Play a sound if we have one
            if (soundOn != null)
                _audio.PlayPvs(soundOn, performer, AudioParams.Default.WithVolume(volumeOn ?? 5f));

            // Drain power and stamina if we have a cost
            _power.TryAddPowerLevel(performer, -powerCostOn);
            _stamina.TakeStaminaDamage(performer, staminaCostOn);
        }
        else // Out of The Dark
        {
            // Remove the DarkSwapped component, the rest is handled in the shutdown event
            _entity.RemoveComponent<ShadowkinDarkSwappedComponent>(performer);
             // Tell other systems we've un DarkSwapped
            RaiseNetworkEvent(new ShadowkinDarkSwappedEvent(_entity.GetNetEntity(performer), false));

            // Play a sound if we have one
            if (soundOff != null)
                _audio.PlayPvs(soundOff, performer, AudioParams.Default.WithVolume(volumeOff ?? 5f));

            // Drain power and stamina if we have a cost
            _power.TryAddPowerLevel(performer, -powerCostOff);
            _stamina.TakeStaminaDamage(performer, staminaCostOff);
        }

        // If we have an event, mark it as handled
        if (args != null)
            args.Handled = true;
    }


    private void OnInvisStartup(EntityUid uid, ShadowkinDarkSwappedComponent component, ComponentStartup args)
    {
        if (component.Pacify)
            EnsureComp<PacifiedComponent>(uid);

        if (component.Invisible)
        {
            SetVisibility(uid, true, true, true);
            SuppressFactions(uid, true);
        }
    }

    private void OnInvisShutdown(EntityUid uid, ShadowkinDarkSwappedComponent component, ComponentShutdown args)
    {
        RemComp<PacifiedComponent>(uid);

        if (component.Invisible)
        {
            SetVisibility(uid, false, true, true);
            SuppressFactions(uid, false);
        }

        // Prevent more updates while we're cleaning up
        component.Darken = false;

        // In case more updates occur for some reason, create a copy of the list to prevent error
        foreach (var light in component.DarkenedLights.ToArray())
        {
            if (!_entity.TryGetComponent<PointLightComponent>(light, out var pointLight) ||
                !_entity.TryGetComponent<ShadowkinLightComponent>(light, out var shadowkinLight))
                continue;

            _darken.ResetLight(pointLight, shadowkinLight);
        }

        // Clear the original array
        component.DarkenedLights.Clear();
    }

    /// <summary>
    ///     Makes the specified entity able to see Shadowkin invisibility.
    /// </summary>
    /// <param name="uid">Entity to modify</param>
    /// <param name="set">Whether the entity can see invisibility</param>
    /// <param name="invisibility">Should the entity be moved to another visibility layer?</param>
    /// <param name="stealth">(Only gets considered if set is true) Adds stealth to the entity</param>
    public void SetVisibility(EntityUid uid, bool set, bool invisibility, bool stealth)
    {
        // We require the visibility component for this to work
        var visibility = EnsureComp<VisibilityComponent>(uid);

        if (set) // Invisible
        {
            // Allow the entity to see DarkSwapped entities
            if (_entity.TryGetComponent(uid, out EyeComponent? eye))
                _eye.SetVisibilityMask(uid, eye.VisibilityMask | (int) VisibilityFlags.DarkSwapInvisibility, eye);
                // _eye.SetVisibilityMask(uid, eye.VisibilityMask | (int) (VisibilityFlags.DarkSwapInvisibility), eye);
                // eye.VisibilityMask |= (int) VisibilityFlags.DarkSwapInvisibility;

            // Make other entities unable to see the entity unless also DarkSwapped
            if (invisibility)
            {
                _visibility.AddLayer(uid, visibility, (int) VisibilityFlags.DarkSwapInvisibility, false);
                _visibility.RemoveLayer(uid, visibility, (int) VisibilityFlags.Normal, false);
            }
            _visibility.RefreshVisibility(uid);

            // If not a ghost, add a stealth shader to the entity
            if (!_entity.TryGetComponent<GhostComponent>(uid, out _) && stealth)
                _stealth.SetVisibility(uid, 0.8f, _entity.EnsureComponent<StealthComponent>(uid));
        }
        else // Visible
        {
            // Remove the ability to see DarkSwapped entities
            if (_entity.TryGetComponent(uid, out EyeComponent? eye))
                _eye.SetVisibilityMask(uid, eye.VisibilityMask & ~(int) VisibilityFlags.DarkSwapInvisibility, eye);
                // _eye.SetVisibilityMask(uid, eye.VisibilityMask | (int) (VisibilityFlags.DarkSwapInvisibility), eye);
                // eye.VisibilityMask &= ~(int) VisibilityFlags.DarkSwapInvisibility;

            // Make other entities able to see the entity again
            if (invisibility)
            {
                _visibility.RemoveLayer(uid, visibility, (int) VisibilityFlags.DarkSwapInvisibility, false);
                _visibility.AddLayer(uid, visibility, (int) VisibilityFlags.Normal, false);
            }
            _visibility.RefreshVisibility(uid);

            // Remove the stealth shader from the entity
            if (!_entity.TryGetComponent<GhostComponent>(uid, out _))
                _stealth.SetEnabled(uid, false);
        }
    }
            // if (canSee)
            //     _eye.SetVisibilityMask(uid, eyeComponent.VisibilityMask | (int) VisibilityFlags.Ghost, eyeComponent);
            // else
            //     _eye.SetVisibilityMask(uid, eyeComponent.VisibilityMask & ~(int) VisibilityFlags.Ghost, eyeComponent);

    /// <summary>
    ///     Remove existing factions on the entity and move them to the power component to add back when removed from The Dark
    /// </summary>
    /// <param name="uid">Entity to modify factions for</param>
    /// <param name="set">Add or remove the factions</param>
    public void SuppressFactions(EntityUid uid, bool set)
    {
        // We require the power component to keep track of the factions
        if (!_entity.TryGetComponent<ShadowkinDarkSwapPowerComponent>(uid, out var component))
            return;

        if (set)
        {
            if (!_entity.TryGetComponent<NpcFactionMemberComponent>(uid, out var factions))
                return;

            // Copy the suppressed factions to the power component
            component.SuppressedFactions = factions.Factions.ToList();

            // Remove the factions from the entity
            foreach (var faction in factions.Factions)
                _factions.RemoveFaction(uid, faction);

            // Add status factions for The Dark to the entity
            foreach (var faction in component.AddedFactions)
                _factions.AddFaction(uid, faction);
        }
        else
        {
            // Remove the status factions from the entity
            foreach (var faction in component.AddedFactions)
                _factions.RemoveFaction(uid, faction);

            // Add the factions back to the entity
            foreach (var faction in component.SuppressedFactions)
                _factions.AddFaction(uid, faction);

            component.SuppressedFactions.Clear();
        }
    }
}
