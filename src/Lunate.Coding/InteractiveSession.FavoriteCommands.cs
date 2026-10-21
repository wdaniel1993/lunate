using Lunate.Ai;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>The settings cap on the ordered favourites list.</summary>
    private const int MaxFavorites = 8;

    /// <summary>
    /// <c>/favorite [id]</c>: appends the active or named catalog model at the end and saves. A
    /// duplicate and a full list are no-op notices; a failed write keeps the previous list in
    /// memory and on disk.
    /// </summary>
    private void Favorite(string argument)
    {
        if (RefusedWhileBusy())
        {
            return;
        }

        if (!TryResolveFavoriteId(argument, out string id))
        {
            return;
        }

        if (_settings.Favorites.Contains(id, StringComparer.Ordinal))
        {
            Notify("already a favorite: " + id);
            return;
        }

        if (_settings.Favorites.Count >= MaxFavorites)
        {
            Notify("favorites are limited to 8", NoticeLevel.Warning);
            return;
        }

        SaveFavorites([.. _settings.Favorites, id], "added favorite: " + id);
    }

    /// <summary>
    /// <c>/unfavorite [id]</c>: removes the active or named model, preserving the order of the
    /// rest. Removing a model that is not a favourite is a no-op notice.
    /// </summary>
    private void Unfavorite(string argument)
    {
        if (RefusedWhileBusy())
        {
            return;
        }

        string id;
        if (argument.Length > 0)
        {
            if (!TryResolveFavoriteId(argument, out id))
            {
                return;
            }
        }
        else
        {
            id = _model.Id;
        }

        if (!_settings.Favorites.Contains(id, StringComparer.Ordinal))
        {
            Notify("not a favorite: " + id);
            return;
        }

        SaveFavorites(
            [
                .. _settings.Favorites.Where(favorite =>
                    !string.Equals(favorite, id, StringComparison.Ordinal)
                ),
            ],
            "removed favorite: " + id
        );
    }

    /// <summary>
    /// Ctrl+P: switches to the next favourite in settings order, wrapping after the last; when
    /// the active model is not in the list, the first favourite is next. An empty list is a
    /// notice, never a run. The switch is the <c>/model</c> path, so the session records it and
    /// the footer follows.
    /// </summary>
    private async ValueTask CycleFavoriteAsync(CancellationToken ct)
    {
        if (RefusedWhileBusy())
        {
            return;
        }

        IReadOnlyList<string> favorites = _settings.Favorites;
        if (favorites.Count == 0)
        {
            Notify("no favorites to cycle — add one with /favorite");
            return;
        }

        int current = -1;
        for (int index = 0; index < favorites.Count; index++)
        {
            if (string.Equals(favorites[index], _model.Id, StringComparison.Ordinal))
            {
                current = index;
                break;
            }
        }

        string next = favorites[(current + 1) % favorites.Count];
        await SwitchModel(next, ct).ConfigureAwait(false);
    }

    private bool TryResolveFavoriteId(string argument, out string id)
    {
        string requested = argument.Length > 0 ? argument : _model.Id;
        try
        {
            ModelInfo? model = _catalog.Find(requested);
            if (model is null)
            {
                Notify("unknown model: " + requested, NoticeLevel.Warning);
                id = string.Empty;
                return false;
            }

            id = model.Id;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            Notify(exception.Message, NoticeLevel.Warning);
            id = string.Empty;
            return false;
        }
    }

    private void SaveFavorites(IReadOnlyList<string> favorites, string success)
    {
        try
        {
            SettingsStore.WriteFavorites(favorites, _options.SettingsPath);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Notify("could not save favorites: " + exception.Message, NoticeLevel.Error);
            return;
        }

        _settings = _settings with { Favorites = favorites };
        // The footer's fav:<position>/<count> segment follows the list immediately.
        UpdateStatus();
        Notify(success);
    }
}
