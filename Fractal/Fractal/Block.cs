using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenGL;

namespace Fractal
{
    struct Block
    {
        public Vector3 position;
        public float yOrientation; //this is the primary orientation axis
        public Vector3 scale; 

        public Vector3[] verticeData;
        public Vector3[] normalData;
        public Vector3[] edgeVerticeData;

        public int ID;

        public bool isSame(Block other)
        {
            if (this.position == other.position && this.ID == other.ID)
                return true;
            else
                return false;
        }
    }

    struct DecompBlock
    {
        public int ID;
        public Vector3 posOffset;
        public float xAngleOffset;
        public Vector3 extraScale;
        public float scaleDownConst;
        public float unitScaleConst;
    }
}
