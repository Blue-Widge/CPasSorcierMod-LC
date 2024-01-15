using System;
using System.Collections;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Logger = UnityEngine.Logger;


namespace CPasSorcierShipMod
{
    [BepInPlugin(_modGUID, _modName, _modVersion)]
    public class CPasSorcierShipModHandler : BaseUnityPlugin
    {
        private const string _modGUID = "BlueWidge.CPasSorcierShipMod";
        private const string _modName = "CPasSorcierShip";
        private const string _modVersion = "1.0.0";

        private readonly Harmony _harmony = new Harmony(_modGUID);
        private static CPasSorcierShipModHandler _instance;

        private static AudioClip _AllezMarcelAudioClip;
        private static AudioClip _LandingAudioClip;
        private static AudioSource _speakerAudioSource;
        private static readonly float FadeDuration = 2.0f;

        private void Awake()
        {
            if (_instance == null)
                _instance = this;
            _harmony.PatchAll(typeof(CPasSorcierShipModHandler));
            _harmony.PatchAll(typeof(StartOfRoundPatch));

            var CPasSorcierAssetBundle =
                AssetBundle.LoadFromMemory(CpasSorcierShipMod.Properties.Resources.CPasSorcierAssetBundle);

            if (!CPasSorcierAssetBundle)
            {
                Logger.LogInfo("Couldn't retrieve CPasSorcierAssetBundle");
                enabled = false;
                return;
            }

            _AllezMarcelAudioClip = CPasSorcierAssetBundle.LoadAsset<AudioClip>("Assets/Resources/AllezMarcel.mp3");
            _LandingAudioClip = CPasSorcierAssetBundle.LoadAsset<AudioClip>("Assets/Resources/Landing.mp3");

            if (_AllezMarcelAudioClip || !_LandingAudioClip || _AllezMarcelAudioClip.LoadAudioData() ||
                _LandingAudioClip.LoadAudioData())
            {
                Logger.LogInfo("Couldn't retrieve all audio clips");
                enabled = false;
            }

            Logger.LogInfo("Successfully loaded CPasSorcierShip");
        }

        [HarmonyPatch(typeof(StartOfRound))]
        internal class StartOfRoundPatch
        {
            private static bool m_isLanding = false;

            [HarmonyPatch(nameof(StartOfRound.StartGame))]
            [HarmonyPostfix]
            private static void PlayAllezMarcelStartGame()
            {
                StartOfRound.Instance.StartCoroutine(PlayAllezMarcel());
            }

            [HarmonyPatch(nameof(StartOfRound.ChangeLevelClientRpc))]
            [HarmonyPostfix]
            private static void PlayAllezMarcelChangeLevel()
            {
                StartOfRound.Instance.StartCoroutine(WaitForChangingLevelConfirmation());
            }

            private static IEnumerator PlayAllezMarcel()
            {
                if (!_speakerAudioSource)
                {
                    _speakerAudioSource = StartOfRound.Instance.speakerAudioSource;
                    if (!_speakerAudioSource)
                    {
                        _instance.Logger.LogError("Couldn't retrieve speaker audiosource in PlayAllezMarcelPatch");
                        yield break;
                    }
                }

                //If already playing then skip
                if (_speakerAudioSource.clip == _AllezMarcelAudioClip)
                    yield break;

                _speakerAudioSource.clip = _AllezMarcelAudioClip;
                _speakerAudioSource.Play();

                //I find it funny with doppler effect
                //_speakerAudioSource.dopplerLevel = 0;
                _instance.Logger.LogInfo("Playing _AllezMarcelAudioClip");

                yield return new WaitUntil(() => m_isLanding ||
                                                 _speakerAudioSource.time >=
                                                 _AllezMarcelAudioClip.length - FadeDuration);

                if (m_isLanding) yield break;

                var startingVolume = _speakerAudioSource.volume;
                _instance.Logger.LogInfo("Calling fade out in PlayAllezMarcel");
                StartOfRound.Instance.StartCoroutine(FadeOut(startingVolume));
            }

            private static IEnumerator WaitForChangingLevelConfirmation()
            {
                yield return new WaitUntil(() => StartOfRound.Instance.travellingToNewLevel);

                StartOfRound.Instance.StartCoroutine(PlayAllezMarcel());
            }

            [HarmonyPatch(nameof(StartOfRound.openingDoorsSequence))]
            [HarmonyPostfix]
            private static void PlayTruckBrakeSound()
            {
                StartOfRound.Instance.StartCoroutine(WaitUntilLanding());
            }

            private static IEnumerator WaitUntilLanding()
            {
                //it activates too late, and shipLanded also is set too late
                //yield return new WaitUntil(() => HUDManager.Instance.quotaAnimator.GetBool("visible"));

                //Waiting to prevent playing sound on loading level
                yield return new WaitForSeconds(2.0f);
                yield return
                    new WaitUntil(() =>
                        StartOfRound.Instance.shipAnimator.transform.position.x <
                        20.0f); // the value is found by testing

                if (!_speakerAudioSource)
                {
                    _speakerAudioSource = StartOfRound.Instance.speakerAudioSource;
                    if (!_speakerAudioSource)
                    {
                        _instance.Logger.LogError("Couldn't retrieve speaker audiosource in PlayTruckBrakeSound");
                        yield break;
                    }
                }

                //Fade between last sound and landing sound
                StartOfRound.Instance.StartCoroutine(FadeFromMarcelToLanding());

                //To get the current position of the ship
                /*while (StartOfRound.Instance.shipHasLanded == false)
                {
                    _instance.Logger.LogInfo("Current position of the ship : " +
                                             StartOfRound.Instance.shipAnimator.transform.position);
                    yield return null;
                }*/
            }

            private static IEnumerator FadeFromMarcelToLanding()
            {
                m_isLanding = true;
                var startingVolume = _speakerAudioSource.volume;
                yield return StartOfRound.Instance.StartCoroutine(FadeOut(startingVolume));
                _speakerAudioSource.Stop();
                _speakerAudioSource.clip = _LandingAudioClip;
                _speakerAudioSource.Play();
                _instance.Logger.LogInfo("Playing _LandingAudioClip");
                yield return StartOfRound.Instance.StartCoroutine(FadeIn(startingVolume));
                yield return new WaitUntil(() => !_speakerAudioSource.isPlaying);
                _speakerAudioSource.PlayOneShot(StartOfRound.Instance.disableSpeakerSFX);
            }
        }

        private static IEnumerator FadeIn(float p_startingVolume)
        {
            var startTime = Time.time;

            while (_speakerAudioSource.volume < p_startingVolume)
            {
                _instance.Logger.LogInfo("Current volume in FadeIn : " + _speakerAudioSource.volume);
                var progress = Mathf.Clamp01((Time.time - startTime) / FadeDuration);
                _speakerAudioSource.volume = progress;
                yield return null;
            }

            _speakerAudioSource.volume = p_startingVolume;
        }

        private static IEnumerator FadeOut(float p_startingVolume)
        {
            var startTime = Time.time;
            while (_speakerAudioSource.volume > 0.0f)
            {
                var progress = Mathf.Clamp01((Time.time - startTime) / FadeDuration);
                _speakerAudioSource.volume = p_startingVolume - progress;
                yield return null;
            }

            _speakerAudioSource.volume = 0.0f;
        }
    }
}