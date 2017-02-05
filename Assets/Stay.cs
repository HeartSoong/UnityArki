using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Stay : MonoBehaviour {
	public GameObject chicken;
	public AudioClip impact;
	       AudioSource audio;
	void Start() {
		audio = GetComponent<AudioSource>();
	}
	void OnTriggerEnter(Collider other) {
		if (other.gameObject.name == "FPSController") {
			chicken.transform.localScale = new Vector3 (50f, 50f, 50f);
			audio.PlayOneShot (impact, 0.7F);
				
			}


	}
}

