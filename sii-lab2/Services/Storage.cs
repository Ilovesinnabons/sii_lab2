namespace sii_lab2;

public static class Storage
{
    // Файлы внутри пакета приложения доступны только на чтение,
    // поэтому при первом запуске копируем их в папку данных приложения.
    public static async Task CopyFromPackageIfMissing(string packageFile, string targetPath)
    {
        if (File.Exists(targetPath))
            return;

        using var src = await FileSystem.OpenAppPackageFileAsync(packageFile);
        using var dst = File.Create(targetPath);
        await src.CopyToAsync(dst);
    }
}
