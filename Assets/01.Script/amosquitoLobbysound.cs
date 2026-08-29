using UnityEngine;
using System.Collections;
public class amosquitoLobbysound : MonoBehaviour
{
    public AudioSource audioSource;

    public float minDelay = 2f;
    public float maxDelay = 6f;

    void Start()
    {
        StartCoroutine(PlayMosquitoSound());
    }

    IEnumerator PlayMosquitoSound()
    {
        while (true)
        {
            float delay = Random.Range(minDelay, maxDelay);
            yield return new WaitForSeconds(delay);

            if (!audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }
    }
}
