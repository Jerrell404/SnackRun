using UnityEngine;
public class DogToy : MonoBehaviour
{
    [SerializeField] private DogNPC dog;
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            dog.WakeUp();
        }
    }
}