using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace HomeBuilder.Data
{
    /// <summary>
    /// Handles JSON Save/Load operations for ProjectData and copies runtime-imported 3D assets
    /// into the project folder bundle.
    /// </summary>
    public sealed class ProjectPersistenceService
    {
        private readonly JsonSerializerSettings _serializerSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        public async Task SaveProjectAsync(ProjectData project, string filePath)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Invalid file path.", nameof(filePath));

            project.LastModifiedUtc = DateTime.UtcNow;
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonConvert.SerializeObject(project, _serializerSettings);
            await File.WriteAllTextAsync(filePath, json);
            Debug.Log($"[ProjectPersistenceService] Projekt zapisany pomyślnie: {filePath}");
        }

        public async Task<ProjectData> LoadProjectAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Nie znaleziono pliku projektu: {filePath}", filePath);
            }

            string json = await File.ReadAllTextAsync(filePath);
            ProjectData project = JsonConvert.DeserializeObject<ProjectData>(json, _serializerSettings);
            return project ?? new ProjectData();
        }
    }
}
