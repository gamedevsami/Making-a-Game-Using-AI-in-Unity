using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class bgscroll : MonoBehaviour
{
    //GameDev Sami
    //Declear variables here.
    [SerializeField] private Renderer meshRenderer;
    [SerializeField] private float speed = 0.1f;

    private void Awake()
    {
        // Auto-assign if not set in Inspector
        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<Renderer>();
        }
    }

    private void Update()
    {
        if (meshRenderer != null)
        {
            meshRenderer.material.mainTextureOffset += new Vector2(speed * Time.deltaTime, 0);
        }
    }
    

}
