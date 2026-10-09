using Gameplay;
using NUnit.Framework;
using UnityEditor;

public class GameplayBalanceProfileTests
{
    [Test]
    public void ProduccionUsaVentanasYVelocidadesParaEspaciosPequenos()
    {
        float[] book = { 16f, 15f, 14f, 14f, 13f, 12f };
        float[] veleth = { 2.2f, 2.4f, 2.6f, 2.7f, 2.9f, 3f };
        float[] retreat = { 2.8f, 3f, 3.1f, 3.3f, 3.4f, 3.6f };

        for (int i = 0; i < 6; i++)
        {
            var night = AssetDatabase.LoadAssetAtPath<NightConfig>(
                $"Assets/Gameplay/Nights/prod/Noche {i + 1}.asset");
            Assert.That(night, Is.Not.Null);
            Assert.That(night.nightDurationSeconds, Is.EqualTo(300f));
            Assert.That(night.bookConsumeSeconds, Is.EqualTo(book[i]));
            Assert.That(night.velethChaseSpeed, Is.EqualTo(veleth[i]));
            Assert.That(night.sorkenRetreatSpeed, Is.EqualTo(retreat[i]));
        }
    }

    [Test]
    public void DesarrolloConservaLaMismaCurvaDeDificultad()
    {
        float previousVeleth = 0f;
        for (int i = 0; i < 6; i++)
        {
            var night = AssetDatabase.LoadAssetAtPath<NightConfig>(
                $"Assets/Gameplay/Nights/dev/Noche {i + 1}.asset");
            Assert.That(night, Is.Not.Null);
            Assert.That(night.nightDurationSeconds, Is.EqualTo(300f));
            Assert.That(night.bookConsumeSeconds, Is.GreaterThanOrEqualTo(6f));
            Assert.That(night.velethChaseSpeed, Is.GreaterThanOrEqualTo(previousVeleth));
            previousVeleth = night.velethChaseSpeed;
        }
    }
}
