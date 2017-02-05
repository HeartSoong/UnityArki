using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class scale : MonoBehaviour {

	private void OnTriggerStay()
	{
		transform.localScale = new Vector3 (50f, 50f, 50f);
	}
		

}
