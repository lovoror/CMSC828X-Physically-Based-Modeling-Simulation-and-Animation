﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Factorization;
using MathNet.Numerics.Data.Text;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
public class Falling_Plastic: MonoBehaviour
{
    // Start is called before the first frame update
    Sound Sound;
    float forceamp;
    float forceoffset=0.1f;   //directly controlled in the scripts
    Vector3[] displacedVertices;

    Matrix<float> G;
    Matrix<float> D;
    Matrix<float> M;
    Vector<float> gain;

    //float Beta = 0.0000000023f;
    //float Alpha = 0.00125f;
    float Beta = 1.45e-10f;
    float Alpha = 0.005f; //rayleigh damping, metal


    //wood? ->float Alpha = 0.15f; 
    int readflag = 0;

    float[] D_data;
    float[] soundsamples;
    void Start()
    {
       

    }

    private void OnEnable()
    {
        if (readflag == 0)
        {
            Control.UseNativeMKL();
            G = DelimitedReader.Read<float>("Gmatrix2.csv", false, ",", false);
            D = DelimitedReader.Read<float>("Dmatrix2.csv", false, ",", false);
            displacedVertices = this.GetComponentInParent<MeshFilter>().sharedMesh.vertices;
            Sound = this.GetComponent<Sound>();
            soundsamples = new float[44100];
            D_data = D.ToRowMajorArray();
            readflag = 0;
        }
    }

    void OnCollisionEnter(Collision other)
    {     
        forceamp = other.impulse.magnitude / Time.fixedDeltaTime;
        Vector3 point = Vector3.zero;
        for(int i=0;i<other.contacts.Length;i++)
        {
            point += other.contacts[i].point;
        }
        point /= other.contacts.Length;  //averaged point contacts
        point += other.impulse.normalized * forceoffset;
        HandleInput(point);
        //other.impulse
        //print("Points colliding: " + other.contacts.Length);
        //print("First point that collided: " + other.contacts[0].point);

    }
    void OnCollisionExit(Collision other)
    {
        //Debug.Log(other.relativeVelocity);
    }
    void HandleInput(Vector3 point)
    {     
      AddForce(point, forceamp);       
    }

    public void AddForce(Vector3 point, float force)
    {
        var F = Vector<float>.Build;
        Vector<float> tmpForce = F.Dense(displacedVertices.Length);

        for (int i = 0; i < displacedVertices.Length; i++)
        {
            AddForceVertex(i, point, force, ref tmpForce);

        }

        Matrix<float> ff = Matrix<float>.Build.Dense(displacedVertices.Length / 3, 3);
        /*for (int i = 0; i < displacedVertices.Length / 3; i++)
        {
            ff[i, 0] = point.x;
            ff[i, 1] = point.y;
            ff[i, 2] = point.z;
        }
        */
        // Debug.DrawLine(Camera.main.transform.position, point);
        //print(tmpForce);
        gain = G.Transpose().Multiply(tmpForce);
        //gain_process();
        //StartCoroutine(SoundSimulator(gain));

        //job system ways
        var samples = new NativeArray<float>(44100, Allocator.Persistent);
        var gainh = new NativeArray<float>(gain.ToArray(), Allocator.Persistent);
        var D_array = new NativeArray<float>(D_data, Allocator.Persistent);
        var job = new Soundplay()
        {
            D_data = D_array,
            gain_data = gainh,
            samplefrequencies = samples,
            alpha = Alpha,
            beta = Beta,
            size = D.RowCount,
        };
        JobHandle jobHandle = job.Schedule();
        jobHandle.Complete();
        job.samplefrequencies.CopyTo(soundsamples);
        Sound.soundplay(soundsamples);
        samples.Dispose();
        gainh.Dispose();
        D_array.Dispose();
    }

    void AddForceVertex(int i, Vector3 point, float force, ref Vector<float> tmpForce)
    {
        Vector3 pointToVertex = point - displacedVertices[i];
        float attenuatedForce = force / (1f + pointToVertex.sqrMagnitude);
        //var f = pointToVertex.normalized * attenuatedForce;
        tmpForce[i] = attenuatedForce;
    }


    IEnumerator SoundPrepration(Vector<float> gain)
    {
        yield return StartCoroutine(SoundSimulator(gain));
        print("here");
    }
    IEnumerator SoundSimulator(Vector<float> gain)
    {
        float[] samples = new float[44100];
        //simulate 1 seconds
        int sampleFreq = 44100;
        //print(D.RowCount);
   
        for (int i = 0; i < D.RowCount; i++)
        {

            float d = 0.5f * (Alpha + Beta * D[i, i]);
            if ((D[i, i] - d * d) < 0 || D[i, i] <= 0) continue;
            float omega = Mathf.Sqrt(D[i, i] - d * d);
            //print(gain[i]);      
            //omega = Mathf.PI * 2 * 2670.117188f;
            /*for (int j = 0; j < samples.Length; j++)
            {
                samples[j] += gain[i] * Mathf.Exp(-d * j) * Mathf.Sin(j * omega / sampleFreq);
            }
            */

        }

        //Sound.soundplay(samples);
        yield return null;

    }

    void gain_process()
    {
        float maxnum = 0.0f;
        maxnum = gain.AbsoluteMaximum();
        for (int i = 0; i < gain.Count; i++)
        {
            gain[i] /= maxnum;
        }
    }

    [BurstCompile]
    public struct Soundplay : IJob
    {
        public NativeArray<float> samplefrequencies;
        [ReadOnly]
        public NativeArray<float> gain_data;
        [ReadOnly]
        public NativeArray<float> D_data;
        [ReadOnly]
        public float alpha;
        [ReadOnly]
        public float beta;
        public int size;
        public void Execute()
        {
            for (int i = 0; i < size; i++)
            {

                float d = 0.5f * (alpha + beta * D_data[i * size + i]);
                if ((D_data[i * size + i] - d * d) < 0 || D_data[i * size + i] <= 0) continue;
                float omega = Mathf.Sqrt(D_data[i * size + i] - d * d);
                //print(gain[i]);      
                //omega = Mathf.PI * 2 * 2670.117188f;
                for (int j = 0; j < samplefrequencies.Length; j++)
                {
                    samplefrequencies[j] += gain_data[i] * Mathf.Exp(-d * j) * Mathf.Sin(j * omega / 44100);
                    //print(samplefrequencies[j]);
                }

            }

        }
    }
}
