﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Gms.Nearby.ExposureNotification;
using Nearby = Android.Gms.Nearby.NearbyClass;

[assembly: UsesPermission(Android.Manifest.Permission.Bluetooth)]

namespace Xamarin.ExposureNotifications
{
	public static partial class ExposureNotification
	{
		static IExposureNotificationClient instance;

		static IExposureNotificationClient Instance
			=> instance ??= Nearby.GetExposureNotificationClient(Application.Context);

		static async Task PlatformStart(IExposureNotificationHandler handler)
		{
			var c = handler.Configuration;

			// TODO: weights are missing

			var config = new ExposureConfiguration.ExposureConfigurationBuilder()
				.SetAttenuationScores(c.AttenuationScores)
				.SetDurationScores(c.DurationScores)
				.SetDaysSinceLastExposureScores(c.DaysScores)
				.SetTransmissionRiskScores(c.TransmissionRiskScores)
				.Build();

			await Instance.StartAsync(config);
		}

		static Task PlatformStop()
			=> Instance.StopAsync();

		static async Task<bool> PlatformIsEnabled()
			=> await Instance.IsEnabledAsync();

		// Gets the contact info of anyone the user had contact with who was diagnosed
		static async Task<IEnumerable<ExposureInfo>> PlatformGetExposureInformation()
		{
			var details = await Instance.GetExposureInformationAsync();

			return details.Select(d => new ExposureInfo(
				DateTimeOffset.UnixEpoch.AddMilliseconds(d.DateMillisSinceEpoch).UtcDateTime,
				TimeSpan.FromMinutes(d.DurationMinutes),
				d.AttenuationValue,
				d.TotalRiskScore,
				(RiskLevel)d.TransmissionRiskLevel));
		}

		// Call this when the user has confirmed diagnosis
		static async Task PlatformSubmitSelfDiagnosis()
		{
			var selfKeys = await Instance.GetTemporaryExposureKeyHistoryAsync();

			await Handler.UploadSelfExposureKeysToServer(
				selfKeys.Select(k => new TemporaryExposureKey(
					k.GetKeyData(),
					k.RollingStartIntervalNumber,
					TimeSpan.Zero, // TODO: TimeSpan.FromMinutes(k.RollingDuration),
					(RiskLevel)k.TransmissionRiskLevel)));
		}

		// Tells the local API when new diagnosis keys have been obtained from the server
		static async Task<ExposureDetectionSummary> PlatformAddDiagnosisKeys(IEnumerable<TemporaryExposureKey> diagnosisKeys)
		{
			var batchSize = await Instance.GetMaxDiagnosisKeyCountAsync();
			var sequence = diagnosisKeys;

			while (sequence.Any())
			{
				var batch = sequence.Take(batchSize);
				sequence = sequence.Skip(batchSize);

				// TODO: RollingDuration is missing

				await Instance.ProvideDiagnosisKeysAsync(
					batch.Select(k => new global::Android.Gms.Nearby.ExposureNotification.TemporaryExposureKey.TemporaryExposureKeyBuilder()
						.SetKeyData(k.KeyData)
						.SetRollingStartIntervalNumber((int)k.RollingStartLong)
						.SetTransmissionRiskLevel((int)k.TransmissionRiskLevel)
						.Build()).ToList());
			}

			var summary = await Instance.GetExposureSummaryAsync();

			// TODO: Reevaluate byte usage here
			return new ExposureDetectionSummary(summary.DaysSinceLastExposure, (ulong)summary.MatchedKeyCount, (byte)summary.MaximumRiskScore);
		}

		static async Task<IEnumerable<TemporaryExposureKey>> PlatformGetTemporaryExposureKeys()
		{
			var exposureKeyHistory = await Instance.GetTemporaryExposureKeyHistoryAsync();

			return exposureKeyHistory.Select(k =>
				new TemporaryExposureKey(
					k.GetKeyData(),
					k.RollingStartIntervalNumber,
					TimeSpan.Zero, // TODO: TimeSpan.FromMinutes(k.RollingDuration * 10),
					(RiskLevel)k.TransmissionRiskLevel));
		}

		internal static async Task<ExposureDetectionSummary> AndroidGetExposureSummary()
		{
			var s = await Instance.GetExposureSummaryAsync();

			// TODO: Verify risk score byte 
			return new ExposureDetectionSummary(s.DaysSinceLastExposure, (ulong)s.MatchedKeyCount, (byte)s.MaximumRiskScore);
		}
	}
}
