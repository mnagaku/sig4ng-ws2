// MIT License Copyright(c) 2022 Filip Slavov, https://github.com/NibbleByte/UnityWiseGit

using DevLocker.VersionControl.WiseGit.Preferences;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseGit
{
	/// <summary>
	/// Renders git overlay icons in the project windows.
	/// Hooks up to Unity file changes API and refreshes when needed to.
	/// </summary>
	[InitializeOnLoad]
	internal static class GitOverlayIcons
	{
		private static GitPreferencesManager.PersonalPreferences m_PersonalPrefs => GitPreferencesManager.Instance.PersonalPrefs;

		private static bool IsActive => m_PersonalPrefs.EnableCoreIntegration && (m_PersonalPrefs.PopulateStatusesDatabase || GitPreferencesManager.Instance.ProjectPrefs.EnableLockPrompt);

		private static bool m_ShowNormalStatusIcons = false;
		private static bool m_ShowExcludeStatusIcons = false;
		private static string[] m_ExcludedPaths = new string[0];

		private static GUIContent m_DataIsIncompleteWarning;

		private static int? m_RefreshProgressId;

		static GitOverlayIcons()
		{
			GitPreferencesManager.Instance.PreferencesChanged += PreferencesChanged;
			GitStatusesDatabase.Instance.DatabaseChanged += OnDatabaseChanged;

			PreferencesChanged();
		}

		private static void PreferencesChanged()
		{
			if (IsActive) {
				EditorApplication.projectWindowItemOnGUI -= ItemOnGUI;
				EditorApplication.projectWindowItemOnGUI += ItemOnGUI;

				m_ShowNormalStatusIcons = GitPreferencesManager.Instance.PersonalPrefs.ShowNormalStatusOverlayIcon;
				m_ShowExcludeStatusIcons = GitPreferencesManager.Instance.PersonalPrefs.ShowExcludedStatusOverlayIcon;
				m_ExcludedPaths = GitPreferencesManager.Instance.PersonalPrefs.Exclude.Concat(GitPreferencesManager.Instance.ProjectPrefs.Exclude).ToArray();
			} else {
				EditorApplication.projectWindowItemOnGUI -= ItemOnGUI;
			}

			OnDatabaseChanged();
		}

		public const string InvalidateDatabaseMenuText = "Assets/Git/Refresh Icons && Locks";
		[MenuItem(InvalidateDatabaseMenuText, false, ContextMenus.GitContextMenusManager.MenuItemPriorityStart + 145)]
		public static void InvalidateDatabaseMenu()
		{
			if (!GitPreferencesManager.Instance.PersonalPrefs.EnableCoreIntegration || !GitPreferencesManager.Instance.PersonalPrefs.PopulateStatusesDatabase) {
				EditorUtility.DisplayDialog("Integration Disabled", "Can't refresh the icons as the WiseGit integration is disabled. Check in the WiseGit preferences.", "Ok");
				return;
			}

			WiseGitIntegration.ClearLastDisplayedError();
			GitPreferencesManager.Instance.TemporarySilenceLockPrompts = false;
			GitStatusesDatabase.Instance.InvalidateDatabase();
			LockPrompting.GitLockPromptDatabase.Instance.ClearKnowledge();

			if (m_RefreshProgressId.HasValue) {
				EditorApplication.update -= UpdateDatabaseRefreshProgress;
				Progress.Remove(m_RefreshProgressId.Value);
				m_RefreshProgressId = null;
			}

			m_RefreshProgressId = Progress.Start("Git Refresh", $"Checking all git statuses...", Progress.Options.Indefinite);
			EditorApplication.update += UpdateDatabaseRefreshProgress;
		}

		private static void UpdateDatabaseRefreshProgress()
		{
			// This called once after OnDatabaseChanged(), as it is in the EditorApplication.update event itself and is already queued for call.
			// It's called after the m_ProgressId is cleared.
			if (m_RefreshProgressId.HasValue) {
				Progress.Report(m_RefreshProgressId.Value, 0.5f);
			}
		}

		private static void OnDatabaseChanged()
		{
			if (m_RefreshProgressId.HasValue) {
				EditorApplication.update -= UpdateDatabaseRefreshProgress;
				Progress.Remove(m_RefreshProgressId.Value);
				m_RefreshProgressId = null;
			}

			EditorApplication.RepaintProjectWindow();
		}

		internal static GUIContent GetDataIsIncompleteWarning()
		{
			if (m_DataIsIncompleteWarning == null) {
				string warningTooltip = "Some or all git overlay icons are skipped as you have too many changes to display.\n" +
					"If you have a lot of unversioned files consider adding them to a git ignore list.\n" +
					"If the server repository has a lot of changes, consider updating.";

				m_DataIsIncompleteWarning = EditorGUIUtility.IconContent("console.warnicon.sml");
				m_DataIsIncompleteWarning.tooltip = warningTooltip;
			}

			return m_DataIsIncompleteWarning;
		}

		private static void ItemOnGUI(string guid, Rect selectionRect)
		{
			if (string.IsNullOrEmpty(guid) || guid.StartsWith("00000000", StringComparison.Ordinal)) {

				if (GitStatusesDatabase.Instance.DataIsIncomplete && guid.Equals(GitStatusesDatabase.ASSETS_FOLDER_GUID, StringComparison.OrdinalIgnoreCase)) {

					var iconRect = new Rect(selectionRect);
					iconRect.height = 20;
					iconRect.x += iconRect.width - iconRect.height - 8f;
					iconRect.width = iconRect.height;
					iconRect.y -= 2f;

					GUI.Label(iconRect, GetDataIsIncompleteWarning());
				}

				// Cause what are the chances of having a guid starting with so many zeroes?!
				//|| guid.Equals(INVALID_GUID, StringComparison.Ordinal)
				//|| guid.Equals(ASSETS_FOLDER_GUID, StringComparison.Ordinal)
				return;
			}

			var statusData = GitStatusesDatabase.Instance.GetKnownStatusData(guid);

			var downloadRepositoryChanges = GitPreferencesManager.Instance.FetchRemoteChanges && !GitPreferencesManager.Instance.NeedsToAuthenticate;
			var lockPrompt = GitPreferencesManager.Instance.ProjectPrefs.EnableLockPrompt;

			//
			// Remote Status
			//
			if (downloadRepositoryChanges && statusData.RemoteStatus != VCRemoteFileStatus.None) {
				var remoteStatusIcon = GitPreferencesManager.Instance.GetRemoteStatusIconContent(statusData.RemoteStatus);

				if (remoteStatusIcon != null) {
					var iconRect = new Rect(selectionRect);
					if (iconRect.width > iconRect.height) {
						iconRect.x += iconRect.width - iconRect.height;
						iconRect.x -= iconRect.height;
						iconRect.width = iconRect.height;
					} else {
						iconRect.width /= 2.4f;
						iconRect.height = iconRect.width;
						var offset = selectionRect.width - iconRect.width;
						iconRect.x += offset;

						iconRect.y -= 4;
					}

					GUI.Label(iconRect, remoteStatusIcon);
				}
			}

			//
			// Lock Status
			//
			if ((downloadRepositoryChanges || lockPrompt) && statusData.LockStatus != VCLockStatus.NoLock) {
				var lockStatusIcon = GitPreferencesManager.Instance.GetLockStatusIconContent(statusData.LockStatus);

				if (lockStatusIcon != null) {
					var iconRect = new Rect(selectionRect);
					if (iconRect.width > iconRect.height) {
						iconRect.x += iconRect.width - iconRect.height;
						iconRect.x -= iconRect.height * 2;
						iconRect.width = iconRect.height;
					} else {
						iconRect.width /= 2.4f;
						iconRect.height = iconRect.width;
						var offset = selectionRect.width - iconRect.width;
						iconRect.x += offset;
						iconRect.y += offset;

						iconRect.y += 2;
					}

					if (GUI.Button(iconRect, lockStatusIcon, EditorStyles.label)) {
						var details = string.Empty;

						foreach (var knownStatusData in GitStatusesDatabase.Instance.GetAllKnownStatusData(guid, false, true, true)) {
							DateTime date;
							string dateStr = knownStatusData.LockDetails.Date;
							if (!string.IsNullOrEmpty(dateStr)) {
								if (DateTime.TryParse(dateStr, out date) ||
								    // This covers failing to parse weird culture date formats like: 2020-09-08 23:32:13 +0300 (??, 08 ??? 2020)
									DateTime.TryParse(dateStr.Substring(0, dateStr.IndexOf("(", StringComparison.OrdinalIgnoreCase)), out date)
								) {
									dateStr = date.ToString("yyyy-MM-dd hh:mm:ss");
								}
							}
							details += $"File: {System.IO.Path.GetFileName(knownStatusData.Path)}\n" +
									  $"Lock Status: {ObjectNames.NicifyVariableName(knownStatusData.LockStatus.ToString())}\n" +
									  $"Owner: {knownStatusData.LockDetails.Owner}\n" +
									  $"Date: {dateStr}\n\n";
									  //$"Message:\n{knownStatusData.LockDetails.Message}\n";
						}
						EditorUtility.DisplayDialog("Git LFS Lock Details", details.TrimEnd('\n'), "Ok");
					}
				}
			}


			//
			// File Status
			//
			VCFileStatus fileStatus = statusData.Status;

			// Handle unknown statuses.
			if (m_ShowNormalStatusIcons && !statusData.IsValid) {
				fileStatus = VCFileStatus.Normal;

				if (m_ExcludedPaths.Length > 0) {
					string path = AssetDatabase.GUIDToAssetPath(guid);
					if (GitPreferencesManager.ShouldExclude(m_ExcludedPaths, path)) {
						fileStatus = m_ShowExcludeStatusIcons ? VCFileStatus.Excluded : VCFileStatus.None;
					}
				}
			}

			GUIContent fileStatusIcon = GitPreferencesManager.Instance.GetFileStatusIconContent(fileStatus);

			// Entries with normal status are present when there is other data to show. Skip the icon if disabled.
			if (!m_ShowNormalStatusIcons && fileStatus == VCFileStatus.Normal) {
				fileStatusIcon = null;
			}

			// Excluded items are added explicitly - their status exists (is known).
			if (!m_ShowExcludeStatusIcons && (fileStatus == VCFileStatus.Excluded || fileStatus == VCFileStatus.Ignored)) {
				fileStatusIcon = null;
			}

			if (fileStatusIcon != null && fileStatusIcon.image != null) {
				var iconRect = new Rect(selectionRect);
				if (iconRect.width > iconRect.height) {
					// Line size: 16px
					iconRect.x -= 3;
					iconRect.y += 7f;
					iconRect.width = iconRect.height = 14f;
				} else {
					// Maximum zoom size: 96 x 110
					iconRect.width = iconRect.width / 3f + 2f;
					iconRect.height = iconRect.width;
					var offset = selectionRect.width - iconRect.width;
					iconRect.y += offset + 1;
				}
				GUI.Label(iconRect, fileStatusIcon);
			}
		}

	}
}
