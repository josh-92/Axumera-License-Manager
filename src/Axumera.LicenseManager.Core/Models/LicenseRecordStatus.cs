namespace Axumera.LicenseManager.Core.Models;

/// <summary>Lifecycle/status of a license record (derived, never persisted as truth).</summary>
public enum LicenseRecordStatus
{
    Active = 0,
    ExpiringSoon = 1,
    Expired = 2,
    Archived = 3,
}