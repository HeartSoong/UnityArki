using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Recover : MonoBehaviour {
	public GameObject chicken;
	public AudioClip impact;
	AudioSource audio;
	void Start() {
		audio = GetComponent<AudioSource> ();
	}
	void OnTriggerExit(Collider other) {
		if (other.gameObject.name == "FPSController") {
			chicken.transform.localScale = new Vector3 (5f, 5f, 5f);
			audio.PlayOneShot (impact, 1F);
		}

	}
}

