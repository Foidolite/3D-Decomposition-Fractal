using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenGL;
using Tao.FreeGlut;
using System.Threading;
using System.IO;

namespace Fractal
{
    class Program
    {
        private static int width = 1280, height = 720;
        private static ShaderProgram program;
        private static List<Block> blocks = new List<Block>();
        private static List<Block> blockRef = new List<Block>();
        private static List<List<DecompBlock>> decompRules = new List<List<DecompBlock>>();
        private static System.Diagnostics.Stopwatch watch;
        private static bool lighting = true, fullscreen = false;
        private static bool left, right, up, down, eKey, qKey;
        private static bool isFlying = false;
        private static float moveSpeed = 3;

        private static VBO<Vector3> drawVertices;
        private static VBO<Vector3> drawNormals;
        private static VBO<Vector3> drawEdges;
        private static VBO<uint> drawElements;
        private static VBO<uint> drawEdgeElements;

        public static int levelNum;

        private static Camera camera;
        static void Main(string[] args)
        {

            while (true)
            {
                try
                {
                    Console.WriteLine("Input the number of levels to recurse. Must be greater than or equal to zero.\r\n" +
                                        "Mouse to look and WASD to move. Hold R to run. F to toggle fullscreen.\r\n" +
                                        "O to toggle between walking and flying. When flying, use E/Q to ascend/descend.");
                    if ((levelNum = Convert.ToInt32(Console.ReadLine())) >= 0)
                    {
                        break;
                    }
                }
                catch
                {
                    Console.WriteLine("Please input an integer greater than or equal to zero.");
                }
            }

            // init GLUT and create window
            Glut.glutInit();
            Glut.glutInitDisplayMode(Glut.GLUT_DOUBLE | Glut.GLUT_DEPTH | Glut.GLUT_MULTISAMPLE);   // multisampling purportedly "makes things beautiful!"
            Glut.glutInitWindowSize(width, height);
            Glut.glutCreateWindow("Fractal");

            // register main loop callbacks
            Glut.glutDisplayFunc(renderScene);
            Glut.glutIdleFunc(idle);
            Glut.glutCloseFunc(onClose);

            //register resize callback
            Glut.glutReshapeFunc(OnReshape);

            // register keyboard callbacks
            Glut.glutKeyboardFunc(OnKeyboardDown);
            Glut.glutKeyboardUpFunc(OnKeyboardUp);

            // register mouse callbacks
            Glut.glutMotionFunc(OnMove);
            Glut.glutPassiveMotionFunc(OnMove);

            //hide mouse
            Glut.glutSetCursor(Glut.GLUT_CURSOR_NONE);

            //enable depth testing
            Gl.Enable(EnableCap.DepthTest);
            
            //enable alpha blending
            Gl.Enable(EnableCap.Blend);
            Gl.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);

            //compile shader
            program = new ShaderProgram(VertexShader, FragmentShader);

            //make a camera
            camera = new Camera(new Vector3(0, 0, 0), Quaternion.Identity); //set the camera starting location here
            camera.SetDirection(new Vector3(0, 0, -1));

            // set the view and projection matrix
            program.Use();
            program["projection_matrix"].SetValue(Matrix4.CreatePerspectiveFieldOfView(0.90f, (float)width / height, 0.01f, 1000f));

            program["light_direction"].SetValue(new Vector3(-0.3f, -0.8f, -0.7f)); //pink
            program["light2_direction"].SetValue(new Vector3(0.7f, -0.8f, 0.3f)); //cyan
            program["light3_direction"].SetValue(new Vector3(0.3f, -0.8f, 0.7f)); //celadon
            program["light4_direction"].SetValue(new Vector3(-0.7f, -0.8f, -0.3f)); //yellow
            program["enable_lighting"].SetValue(lighting);

            //set background color
            Gl.ClearColor(0.99f, 0.93f, 0.85f, 1f);

            // load each block's model and decomposition rule from files and add to blockRef
            int fCount = Directory.GetFiles("assets", "*", SearchOption.TopDirectoryOnly).Length;

            for (int n = 0; n < fCount/2; ++n)
            {
                List<Vector3> indexVertices = new List<Vector3>();
                List<Vector3> indexNormals = new List<Vector3>();
                List<Vector3> vertices = new List<Vector3>();
                List<Vector3> normals = new List<Vector3>();
                List<Vector3> edgeVertices = new List<Vector3>();

                StreamReader sr = new StreamReader("assets/" + n.ToString() + ".txt");
                while (!sr.EndOfStream)
                {
                    string[] fields = sr.ReadLine().Split();
                    if (fields[0] == "v") //a vertex
                    {
                        Vector3 vertex = new Vector3(Convert.ToSingle(fields[1]), Convert.ToSingle(fields[2]), Convert.ToSingle(fields[3]));
                        indexVertices.Add(vertex);
                    }
                    else if (fields[0] == "vn") //a vertex normal
                    {
                        Vector3 normal = new Vector3(Convert.ToSingle(fields[1]), Convert.ToSingle(fields[2]), Convert.ToSingle(fields[3]));
                        indexNormals.Add(normal);
                    }
                    else if (fields[0] == "f") //a face
                    {
                        for (int i = 1; i < fields.Length; ++i)
                        {
                            string[] indices = fields[i].Split('/');
                            vertices.Add(indexVertices[Convert.ToInt32(indices[0]) - 1]);
                            normals.Add(indexNormals[Convert.ToInt32(indices[2]) - 1]);
                        }
                    }
                    else if (fields[0] == "l") //an edge
                    {
                        edgeVertices.Add(indexVertices[Convert.ToInt32(fields[1]) - 1]);
                        edgeVertices.Add(indexVertices[Convert.ToInt32(fields[2]) - 1]);
                    }
                }
                sr.Close();

                //create the block object
                Block newBlock = new Block();
                newBlock.position = new Vector3(0, 0, 0);
                newBlock.yOrientation = 0;
                newBlock.scale = new Vector3(1, 1, 1);
                newBlock.ID = n;

                //fill the block object with data loaded from .obj above
                newBlock.verticeData = vertices.ToArray();
                newBlock.normalData = normals.ToArray();

                newBlock.edgeVerticeData = edgeVertices.ToArray();

                blockRef.Add(newBlock);

                //load the decomposition rule for this block
                List<DecompBlock> newDRule = new List<DecompBlock>();
                float scaleConst = 1;
                float unitConst = 1;

                sr = new StreamReader("assets/" + n.ToString() + "rule.txt");
                while (!sr.EndOfStream)
                {
                    string[] fields = sr.ReadLine().Split();
                    if (fields[0] != "#") //check its not a comment
                    {
                        if (fields[0] == "!")
                        {
                            //setting scale down values. e.g. if set to 1/3 and 2/3, each new block would be scaled down
                            //by 1/3, and each 1 unit offset written in the rule file would be scaled to move the block 2/3 units instead.
                            //default 1 and 1: no scaling applied.
                            scaleConst = Convert.ToSingle(fields[1]);
                            unitConst = Convert.ToSingle(fields[2]);
                        }
                        else
                        {
                            //load in fields
                            DecompBlock newDBlock = new DecompBlock();
                            newDBlock.ID = Convert.ToInt32(fields[0]);
                            newDBlock.posOffset = new Vector3(Convert.ToSingle(fields[1]), Convert.ToSingle(fields[2]), Convert.ToSingle(fields[3]));
                            newDBlock.xAngleOffset = Convert.ToSingle(fields[4]);
                            newDBlock.extraScale = new Vector3(1, 1, 1);
                            newDBlock.unitScaleConst = unitConst;   
                            newDBlock.scaleDownConst = scaleConst;
                            if (fields.Length > 5)
                                newDBlock.extraScale = new Vector3(Convert.ToInt32(fields[5]), Convert.ToInt32(fields[6]), Convert.ToInt32(fields[7]));

                            //add block to rule
                            newDRule.Add(newDBlock);
                        }
                    }
                }
                sr.Close();

                decompRules.Add(newDRule);
            }

            Block seedBlock = blockRef[0];

            blocks = Decompose(seedBlock, levelNum);

            //merge all vertices and normals into a an interleaved vbo
            List<Vector3> preVertices = new List<Vector3>();
            List<Vector3> preNormals = new List<Vector3>();
            List<Vector3> preEdges = new List<Vector3>();

            foreach (Block block in blocks)
            {
                foreach (Vector3 point in block.verticeData)
                {
                    Vector4 preVertex = (new Vector4(point, 1)
                                    * Matrix4.CreateScaling(block.scale)
                                    * Matrix4.CreateRotationY(block.yOrientation)
                                    + new Vector4(block.position, 1));
                    preVertices.Add(new Vector3(preVertex.Get(0), preVertex.Get(1), preVertex.Get(2)));
                }

                foreach (Vector3 normal in block.normalData)
                {
                    Vector4 preNormal = (new Vector4(normal, 1)
                                    * Matrix4.CreateScaling(new Vector3(Math.Sign(block.scale[0]),
                                                                        Math.Sign(block.scale[1]), 
                                                                        Math.Sign(block.scale[2])))
                                    * Matrix4.CreateRotationY(block.yOrientation));

                    preNormals.Add(new Vector3(Convert.ToSingle(Math.Round(preNormal.Get(0))), 
                                            Convert.ToSingle(Math.Round(preNormal.Get(1))), 
                                            Convert.ToSingle(Math.Round(preNormal.Get(2)))));
                }

                foreach (Vector3 point in block.edgeVerticeData)
                {
                    Vector4 preVertex = (new Vector4(point, 1)
                                    * Matrix4.CreateScaling(block.scale)
                                    * Matrix4.CreateRotationY(block.yOrientation)
                                    + new Vector4(block.position, 1));
                    preEdges.Add(new Vector3(preVertex.Get(0), preVertex.Get(1), preVertex.Get(2)));
                }
            }

            //exportOBJ(preVertices, preNormals); //exports fractal if left uncommented

            drawVertices = new VBO<Vector3>(preVertices.ToArray());
            drawNormals = new VBO<Vector3>(preNormals.ToArray());
            drawEdges = new VBO<Vector3>(preEdges.ToArray());

            List<uint> drawOrder = new List<uint>();
            for (int x = 0; x < preVertices.Count; ++x)
                drawOrder.Add(Convert.ToUInt32(x));
            drawElements = new VBO<uint>(drawOrder.ToArray(), BufferTarget.ElementArrayBuffer);

            drawOrder.Clear();
            for (int x = 0; x < preEdges.Count; ++x)
                drawOrder.Add(Convert.ToUInt32(x));
            drawEdgeElements = new VBO<uint>(drawOrder.ToArray(), BufferTarget.ElementArrayBuffer);

            watch = System.Diagnostics.Stopwatch.StartNew();

            // enter GLUT event processing cycle
            Glut.glutMainLoop();
        }

        public static List<Block> Decompose(Block block, int level)
        {
            if (level == 0)
            {
                return new List<Block>(new Block[] {block});
            }
            else
            {
                List<Block> returnList = new List<Block>();

                float baseUnit = block.scale[0]; 
                Vector3 basePos = block.position;
                float baseAngle = block.yOrientation;
                float cos = Convert.ToSingle(Math.Cos(baseAngle));
                float sin = Convert.ToSingle(Math.Sin(baseAngle));
                float isFlipped = Math.Sign(block.scale[1]);

                List<DecompBlock> myRule = decompRules[block.ID];

                for (int i = 0; i < myRule.Count(); ++i)
                {
                    float scaledBaseUnit = baseUnit * myRule[i].unitScaleConst;
                    Block newBlock = blockRef[myRule[i].ID];
                    newBlock.position = basePos + new Vector3(scaledBaseUnit * (myRule[i].posOffset[0] * cos + myRule[i].posOffset[2] * sin), 
                                                                scaledBaseUnit * myRule[i].posOffset[1] * isFlipped, 
                                                                scaledBaseUnit * (myRule[i].posOffset[2] * cos + myRule[i].posOffset[0] * -sin));
                    newBlock.yOrientation = baseAngle + (Convert.ToSingle(Math.PI)/2) * myRule[i].xAngleOffset;
                    newBlock.scale = (block.scale * myRule[i].scaleDownConst) * myRule[i].extraScale;

                    List<Block> decomposed = Decompose(newBlock, level - 1);

                    foreach (Block dBlock in decomposed)
                    {
                        for (int n = 0; n < returnList.Count(); ++n)
                        {
                            if (dBlock.isSame(returnList[n]))
                                break;
                            if (n == returnList.Count() - 1)
                                returnList.Add(dBlock);
                        }

                        if (returnList.Count() == 0)
                            returnList.Add(dBlock);
                    }
                }

                return returnList;
            }
        }

        //This export function does no cleanup. Intersecting or hidden faces will not be resolved or deleted.
        public static void exportOBJ(List<Vector3> vertices, List<Vector3> normals)
        {
            List<Vector3> indexVerts = new List<Vector3>();
            List<Vector3> indexNorms = new List<Vector3>();
            List<string> faces = new List<string>();

            for (int i = 0; i < vertices.Count; i = i + 3)
            {
                string face = "f";

                int normalIndex = -1;
                for (int n = 0; n < indexNorms.Count; ++n)
                {
                    if (normals[i] == indexNorms[n])
                    {
                        normalIndex = n;
                        break;
                    }
                }
                if (normalIndex == -1)
                {
                    normalIndex = indexNorms.Count;
                    indexNorms.Add(normals[i]);
                }

                for (int j = 0; j < 3; ++j)
                {
                    int vertexIndex = -1;
                    for (int n = 0; n < indexVerts.Count; ++n)
                    {
                        if (vertices[i + j] == indexVerts[n])
                        {
                            vertexIndex = n;
                            break;
                        }
                    }
                    if (vertexIndex == -1)
                    {
                        vertexIndex = indexVerts.Count;
                        indexVerts.Add(vertices[i + j]);
                    }

                    face = face + " " + (vertexIndex + 1).ToString() + "//" + (normalIndex + 1).ToString();
                }

                faces.Add(face);
            }

            StreamWriter sw = new StreamWriter("out.txt");
            foreach (Vector3 indexVert in indexVerts)
            {
                sw.WriteLine("v " + indexVert[0] + " " + indexVert[1] + " " + indexVert[2]);
            }
            foreach (Vector3 indexNorm in indexNorms)
            {
                sw.WriteLine("vn " + indexNorm[0] + " " + indexNorm[1] + " " + indexNorm[2]);
            }
            sw.WriteLine("s off");
            foreach (string face in faces)
            {
                sw.WriteLine(face);
            }
            sw.Close();
        }

        private static void renderScene()
        {
            watch.Stop();
            float deltaTime = (float)watch.ElapsedTicks / System.Diagnostics.Stopwatch.Frequency;
            watch.Restart();

            Vector3 preLoc = camera.position;
            float currentUnit = blocks[0].scale[0];

            // update our camera's position
            if (down) camera.MoveRelative(Vector3.UnitZ * deltaTime * currentUnit * moveSpeed);
            if (up) camera.MoveRelative(-Vector3.UnitZ * deltaTime * currentUnit * moveSpeed);
            if (left) camera.MoveRelative(-Vector3.UnitX * deltaTime * currentUnit * moveSpeed);
            if (right) camera.MoveRelative(Vector3.UnitX * deltaTime * currentUnit * moveSpeed);
            if (isFlying)
            {
                if (eKey) camera.MoveRelative(Vector3.UnitY * deltaTime * currentUnit * moveSpeed);
                if (qKey) camera.MoveRelative(-Vector3.UnitY * deltaTime * currentUnit * moveSpeed);
            }

            if (!isFlying) //figure out the camera's height based on the fractal geometry
            {
                List<float> surfaces = new List<float>();
                float camX = camera.position[0], camZ = camera.position[2];

                for (int i = 0; i < blocks.Count(); ++i)
                {
                    float blockX = blocks[i].position[0];
                    float blockZ = blocks[i].position[2];
                    if (Math.Sqrt(Math.Pow(blockX - camX, 2) +
                        Math.Pow(blockZ - camZ, 2)) < blocks[i].scale[0] * 1.5) //check to forgo calculations for blocks outside of user range
                    {
                        for (int n = 0; n < blocks[i].verticeData.Length; n = n + 3) //for each face
                        {
                            if (blocks[i].normalData[n][1] > 0) //if the normal is pointing upwards
                            {
                                //determine the determinant between each edge of the face and the camera position.
                                //if the determinant lies on the same side of each edge, the camera is contained in the face when looking from above.
                                float[] determinants = new float[3];
                                float transformedY = 0;
                                for (int x = 0; x < 3; ++x)
                                {
                                    Vector4 point1 = (new Vector4(blocks[i].verticeData[n + x], 1)
                                        * Matrix4.CreateScaling(blocks[i].scale)
                                        * Matrix4.CreateRotationY(blocks[i].yOrientation))
                                        + new Vector4(blocks[i].position, 1);
                                    Vector4 point2 = (new Vector4(blocks[i].verticeData[n + (x + 1) % 3], 1) 
                                        * Matrix4.CreateScaling(blocks[i].scale)
                                        * Matrix4.CreateRotationY(blocks[i].yOrientation))
                                        + new Vector4(blocks[i].position, 1);
                                    determinants[x] = (point2.Get(0) - point1.Get(0)) * (camZ - point1.Get(2)) - (point2.Get(2) - point1.Get(2)) * (camX - point1.Get(0));
                                    transformedY = point1.Get(1);
                                }

                                if ((Math.Sign(determinants[0]) == Math.Sign(determinants[1]) && Math.Sign(determinants[1]) == Math.Sign(determinants[2])) ||
                                    determinants[0] == 0 || determinants[1] == 0 || determinants[2] == 0)
                                {
                                    surfaces.Add(transformedY);
                                }
                            }
                        }
                    }
                }

                //loop through the surfaces and find the highest one the camera is still above.
                float highest = float.NegativeInfinity;
                for (int i = 0; i < surfaces.Count(); ++i)
                {
                    if ((camera.position[1]) > surfaces[i] && surfaces[i] > highest || camera.position[1] == 0)
                        highest = surfaces[i];
                }

                if (highest == float.NegativeInfinity) //camera is not above any surfaces
                    camera.position = preLoc;
                else
                    camera.destY = highest + currentUnit*2.5f;

                camera.position[1] = camera.position[1] + Convert.ToSingle(Math.Pow((camera.destY - camera.position[1]) / 10, 1));
            }


            Gl.Viewport(0, 0, width, height);
            Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // use our shader program
            Gl.UseProgram(program);

            program["view_matrix"].SetValue(camera.ViewMatrix);
            program["cameraPosition"].SetValue(camera.position);

            //draw faces
            program["model_matrix"].SetValue(Matrix4.CreateTranslation(new Vector3(0, 0, 0)));
            program["enable_lighting"].SetValue(lighting);
            program["isEdge"].SetValue(1f);

            Gl.BindBufferToShaderAttribute(drawVertices, program, "vertexPosition");
            Gl.BindBufferToShaderAttribute(drawNormals, program, "vertexNormal");
            Gl.BindBuffer(drawElements);

            Gl.DrawElements(BeginMode.Triangles, drawElements.Count, DrawElementsType.UnsignedInt, IntPtr.Zero);

            //draw edges
            program["enable_lighting"].SetValue(false);
            program["isEdge"].SetValue(0.1f);

            Gl.BindBufferToShaderAttribute(drawEdges, program, "vertexPosition");
            Gl.BindBuffer(drawEdgeElements);

            Gl.DrawElements(BeginMode.Lines, drawEdgeElements.Count, DrawElementsType.UnsignedInt, IntPtr.Zero);

            Glut.glutSwapBuffers();
        }

        private static void idle()
        {
            Glut.glutPostRedisplay();
            Thread.Sleep(1);
        }

        private static void onClose()
        {
            // dispose of all of the resources that were created
            drawVertices.Dispose();
            drawNormals.Dispose();
            drawElements.Dispose();
            drawEdges.Dispose();
            drawEdgeElements.Dispose();

            program.DisposeChildren = true;
            program.Dispose();
        }

        private static void OnReshape(int width, int height)
        {
            Program.width = width;
            Program.height = height;
        }

        private static void OnKeyboardDown(byte key, int x, int y)
        {
            if (key == 'w') up = true;
            else if (key == 's') down = true;
            else if (key == 'd') right = true;
            else if (key == 'a') left = true;
            else if (key == 'e') eKey = true;
            else if (key == 'q') qKey = true;
            else if (key == 'r') moveSpeed = 6;
            else if (key == 'o') isFlying = !isFlying;
            else if (key == 27) Glut.glutLeaveMainLoop();
        }

        private static void OnKeyboardUp(byte key, int x, int y)
        {
            if (key == 'w') up = false;
            else if (key == 's') down = false;
            else if (key == 'd') right = false;
            else if (key == 'a') left = false;
            else if (key == 'e') eKey = false;
            else if (key == 'q') qKey = false;
            else if (key == 'r') moveSpeed = 3;
            else if (key == 'l') lighting = !lighting;
            else if (key == 'f')
            {
                fullscreen = !fullscreen;
                if (fullscreen) Glut.glutFullScreen();
                else
                {
                    Glut.glutPositionWindow(0, 0);
                    Glut.glutReshapeWindow(1280, 720);
                }
            }
        }

        // This variable is hack to stop glutWarpPointer from triggering an event callback to Mouse(...)
        // This avoids it being called recursively and hanging up the event loop
        static bool just_warped = false;
        private static void OnMove(int x, int y)
        {
            if(just_warped) 
            {
                just_warped = false;
                return;
            }

            int dx = x - width/2;
            int dy = y - height/2;

            float yaw = dx * -0.002f;
            camera.Yaw(yaw);

            float pitch = dy * -0.002f;
            camera.Pitch(pitch);

            Glut.glutWarpPointer(width/2, height/2);

            just_warped = true;
        }

        //shader definitions
        public static string VertexShader = @"
in vec3 vertexPosition;
in vec3 vertexNormal;

out vec3 normal;
out float dist;

uniform mat4 projection_matrix;
uniform mat4 view_matrix;
uniform mat4 model_matrix;
uniform vec3 cameraPosition;

void main(void)
{
    dist = distance((model_matrix * vec4(vertexPosition, 1)).xyz, cameraPosition);
    normal = normalize((model_matrix * vec4(floor(vertexNormal), 0)).xyz);
    gl_Position = projection_matrix * view_matrix * model_matrix * vec4(vertexPosition, 1);
}
";

        public static string FragmentShader = @"
uniform vec3 light_direction;
uniform vec3 light2_direction;
uniform vec3 light3_direction;
uniform vec3 light4_direction;
uniform bool enable_lighting;
uniform float isEdge;

in vec3 normal;
in float dist;

out vec4 fragment;

void main(void)
{
    float diffuse = pow(max(dot(normal, -light_direction), 0),0.05);
    float ambient = 0.5;
    float lighting = (enable_lighting ? max(diffuse, ambient) : 1);

    float diffuse2 = pow(max(dot(normal, -light2_direction), 0),0.05);
    float lighting2 = (enable_lighting ? max(diffuse2, ambient) : 1);

    float diffuse3 = pow(max(dot(normal, -light3_direction), 0),0.05);
    float lighting3 = (enable_lighting ? max(diffuse3, ambient) : 1);

    float diffuse4 = pow(max(dot(normal, -light4_direction), 0),0.05);
    float lighting4 = (enable_lighting ? max(diffuse4, ambient) : 1);

    float alpha = 5 - dist;
    if (alpha > 5)
        alpha = 1;
    else
        alpha = alpha/5;

    if (alpha <= 0)
        alpha = 0.001;

    float edgeFactor = isEdge;
    if (isEdge < 1)
        edgeFactor = edgeFactor*(1/alpha);
    if (edgeFactor*(1/alpha) > 1)
        edgeFactor = 1;

    fragment = vec4(vec3(1, 0.9, 0.8)* lighting * edgeFactor * (1.2 - alpha/1.5), 1)/3 + 
                vec4(vec3(0.95, 1, 1)* lighting2 * edgeFactor * (1.2 - alpha/1.5), 1)/3 +
                vec4(vec3(0.9, 1, 0.91)* lighting3 * edgeFactor * (1.2 - alpha/1.5), 1)/3 +
                vec4(vec3(1, 1, 0.8)* lighting4 * edgeFactor * (1.2 - alpha/1.5), 1)/3 ;
}
";
    }
}