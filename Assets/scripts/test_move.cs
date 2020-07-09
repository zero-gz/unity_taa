using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class test_move : MonoBehaviour {
    public float distance = 1.0f;
    public float time_scale = 1.0f;
    private Vector3 org_pos;
	// Use this for initialization
	void Start () {
        org_pos = transform.position;
    }
	
	// Update is called once per frame
    private void Update()
    {
        this.transform.position = org_pos + new Vector3(Mathf.Sin(Time.time * time_scale) * distance, 0, 0);
    }
}
