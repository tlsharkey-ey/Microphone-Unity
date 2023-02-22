using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public class MicrophoneGUI : MonoBehaviour
{
    [SerializeField]
    private Button RecordButton;
    [SerializeField]
    private RawImage RecordIcon;
    private Color RECORDING_COLOR = Color.red;
    private Color IDLE_COLOR;
    [SerializeField]
    private TMP_Dropdown Devices;
    private int RECORD_FREQUENCY = 44100;
    private int MAX_RECORD_TIME = 1800; // 30 minutes
    private float startTime = 0;
    private object RecordingLock = new object();
    private Queue<AudioClip> RecordingProcessed = new Queue<AudioClip>();
    private AudioClip workingClip;


    public bool IsRecording => Microphone.IsRecording(SelectedDevice);
    public string SelectedDevice => Devices.options[Devices.value].text;
    public UnityEvent<AudioClip> OnRecorded;


    private void Awake()
    {
        // Setup Mic
#if UNITY_WEBGL && !UNITY_EDITOR
        Microphone.Init();
        Microphone.QueryAudioInput();
#endif
        this.GetPermissions();

        // Setup GUI
        RecordButton.onClick.AddListener(this.HandleClick);
        this.UpdateDevices();
        this.IDLE_COLOR = RecordIcon.color;
    }

    private void Update()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Microphone.Update();
#endif
        // Process recording queue
        while (RecordingProcessed.Count > 0)
        {
            AudioClip clip;
            lock (RecordingLock)
            {
                clip = RecordingProcessed.Dequeue();
            }
            Debug.Log("[MicrophoneGUI] Recorded " + clip.length + " seconds of audio");
            OnRecorded.Invoke(clip);
        }

    }



    private void HandleClick()
    {
        if (IsRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    private void StartRecording()
    {
        // Start recording
        workingClip = Microphone.Start(SelectedDevice, false, MAX_RECORD_TIME, RECORD_FREQUENCY);
        this.startTime = Time.time;
        Devices.enabled = false;
        RecordIcon.color = RECORDING_COLOR;
        StartCoroutine(RecordingAnimation());
    }

    private void StopRecording()
    {
        // Stop recording
        Microphone.End(SelectedDevice);
        Devices.enabled = true;
        RecordIcon.color = IDLE_COLOR;

        Trim(workingClip);
    }

    private IEnumerator RecordingAnimation()
    {
        float[] data = new float[256];
        while (IsRecording)
        {
            // get data from microphone in 256 sample chunks
            // #if UNITY_WEBGL && !UNITY_EDITOR
            //             if (!Mic.GetData(data)) continue;
            // #else
            //             float currentDuration = Time.time - startTime;
            //             int currentPos = (int)(currentDuration * RECORD_FREQUENCY);
            //             int micPosition = currentPos - (256 + 1); // null check
            //             if (micPosition < 0) continue;
            //             Mic.RecordingClip.GetData(data, micPosition);
            // #endif
#if UNITY_WEBGL && !UNITY_EDITOR
            float volume = Microphone.GetVolume(SelectedDevice);
#else
            float currentDuration = Time.time - startTime;
            int currentPos = (int)(currentDuration * RECORD_FREQUENCY);
            int sampleStart = currentPos - (data.Length + 1); // null check
            if (sampleStart < 0) continue;
            workingClip.GetData(data, sampleStart);

            // get volume
            float a = 0;
            foreach (float s in data)
            {
                a += Mathf.Abs(s);
            }
            float volume = a / 256;
            // float volume = Mathf.Max(data);
#endif

            // set scale of record icon
            RecordIcon.transform.localScale = Vector3.one * (1 + volume * 2);
            yield return new WaitForEndOfFrame();
        }
    }

    private void UpdateDevices()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Devices.enabled = false;
#else
        Devices.ClearOptions();
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        for (int i = 0; i < Microphone.devices.Length; i++)
        {
            options.Add(new TMP_Dropdown.OptionData(Microphone.devices[i]));
        }
        Devices.AddOptions(options);
#endif
    }

    private void GetPermissions()
    {

    }




    public void Trim(AudioClip clip)
    {
        // Get clip data
        var tmp_samples = new float[clip.samples];
        clip.GetData(tmp_samples, 0);
        List<float> samples = new List<float>(tmp_samples);

        // Trim extra time from end
        float duration = Time.time - startTime;
        int maxSamples = Mathf.Min((int)(duration * clip.frequency), samples.Count);
        samples.RemoveRange(maxSamples, samples.Count - maxSamples);

        // create clip with trimmed data and enqueue it
        AudioClip recording = AudioClip.Create("Recording", maxSamples, clip.channels, clip.frequency, false);
        recording.SetData(samples.ToArray(), 0);

        // enqueue callback
        lock (RecordingLock)
        {
            RecordingProcessed.Enqueue(recording);
        }
    }

    // TrimSilence taken from https://gist.github.com/darktable/2317063
    public void TrimSilence(AudioClip clip, float min)
    {
        // Get clip data
        var tmp_samples = new float[clip.samples];
        clip.GetData(tmp_samples, 0);
        List<float> samples = new List<float>(tmp_samples);

        // Trim silence from start
        int i;
        for (i = 0; i < samples.Count; i++)
        {
            if (Mathf.Abs(samples[i]) > min)
            {
                break;
            }
        }
        samples.RemoveRange(0, i);

        // Trim silence from end
        float duration = Time.time - startTime;
        int maxSamples = Mathf.Min((int)(duration * clip.frequency), samples.Count);
        for (i = maxSamples - 1; i > 0; i--)
        {
            if (Mathf.Abs(samples[i]) > min)
            {
                break;
            }
        }
        samples.RemoveRange(i, samples.Count - i);

        // overwrite clip with trimmed data
        int channels = clip.channels;
        int hz = clip.frequency;
        bool stream = clip.loadType == AudioClipLoadType.Streaming;
        clip.SetData(samples.ToArray(), 0);

        // enqueue callback
        lock (RecordingLock)
        {
            RecordingProcessed.Enqueue(clip);
        }
    }
}
