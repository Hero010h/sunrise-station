using Content.Server.CriminalRecords.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared._Sunrise.AddWantedStatus;
using Content.Shared.Actions;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Content.Shared.Inventory;

namespace Content.Server._Sunrise.AddWantedStatus;

public sealed partial class AddWantedStatusSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly CriminalRecordsSystem _criminalRecords = default!;
    [Dependency] private readonly StationRecordsSystem _records = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly RadioSystem _radio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AddWantedStatusComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AddWantedStatusComponent, AddWantedEvent>(OnAddWanted);
        SubscribeLocalEvent<AddWantedStatusComponent, GetItemActionsEvent>(OnGetItemActions);
    }

    private void OnMapInit(Entity<AddWantedStatusComponent> ent, ref MapInitEvent args)
    {
        var (uid, comp) = ent;
        if (string.IsNullOrEmpty(comp.Action))
            return;

        _actions.AddAction(uid, ref comp.ActionEntity, comp.Action);

    }

    private void OnAddWanted(Entity<AddWantedStatusComponent> ent, ref AddWantedEvent args)
    {
        var target = args.Target;
        var performer = args.Performer;
        if (!HasComp<HumanoidAppearanceComponent>(target))
            return;

        if (_station.GetOwningStation(performer) is not { } station)
            return;

        var target_name = GetDisplayName(target);

        foreach (var (key, record) in _records.GetRecordsOfType<GeneralStationRecord>(station))
        {
            if (record.Name != target_name)
                continue;

            var recordKey = new StationRecordKey(key, station);
            var reason = Loc.GetString("criminal-records-reason-visor");

            if (_criminalRecords.TryChangeStatus(recordKey, SecurityStatus.Wanted, reason))
                SendRadioMessage(ent, reason, performer, recordKey);
        }
    }

    private void OnGetItemActions(Entity<AddWantedStatusComponent> ent, ref GetItemActionsEvent args)
    {
        if (args.InHands || args.SlotFlags == SlotFlags.POCKET)
            return;

        args.AddAction(ent.Comp.ActionEntity);
    }

    private string GetDisplayName(EntityUid ent)
    {
        var metaDataName = MetaData(ent).EntityName;
        if (!TryComp<IdentityComponent>(ent, out var comp))
            return metaDataName;

        var identity_uid = comp.IdentityEntitySlot.ContainedEntity;

        if (identity_uid == null)
            return metaDataName;

        return MetaData(identity_uid.Value).EntityName;
    }

    private void SendRadioMessage(EntityUid sender, string? reason, EntityUid officerUid, StationRecordKey key)
    {
        var wantedName = "Unknown";
        var wantedJobName = "Unknown";
        var officer = "Unknown";

        // Wanted name and job name
        if (_records.TryGetRecord<GeneralStationRecord>(key, out var entry))
        {
            wantedName = entry.Name;
            wantedJobName = entry.JobTitle;
        }

        // Officer
        var getIdEvent = new TryGetIdentityShortInfoEvent(null, officerUid);
        RaiseLocalEvent(getIdEvent);
        if (getIdEvent.Title != null)
            officer = getIdEvent.Title;

        // Reason
        if (string.IsNullOrWhiteSpace(reason))
            reason = "Unknown";

        var loc_args = new (string, object)[] { ("name", wantedName), ("officer", officer), ("reason", reason), ("job", wantedJobName) };
        _radio.SendRadioMessage(sender, Loc.GetString("criminal-records-console-wanted", loc_args), "Security", sender);
    }
}
