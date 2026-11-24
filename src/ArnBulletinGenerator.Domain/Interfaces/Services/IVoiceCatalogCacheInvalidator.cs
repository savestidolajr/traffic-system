namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Invalidates the process-wide cached provider voice catalogue after voice mutations.</summary>
public interface IVoiceCatalogCacheInvalidator
{
    void InvalidateVoiceCatalog();
}
