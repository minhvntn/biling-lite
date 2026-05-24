const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const promos = await prisma.timeBasedPromotion.findMany();
  console.log('Current promotions in database:');
  console.log(JSON.stringify(promos, null, 2));

  let updatedCount = 0;
  for (const promo of promos) {
    if (promo.daysOfWeek && promo.daysOfWeek.includes(7)) {
      // Replace 7 with 0
      let newDays = promo.daysOfWeek.map(d => d === 7 ? 0 : d);
      // Remove duplicates
      newDays = Array.from(new Set(newDays)).sort((a, b) => a - b);
      
      console.log(`Updating promotion "${promo.name}" (${promo.id}):`);
      console.log(`  Old daysOfWeek: ${JSON.stringify(promo.daysOfWeek)}`);
      console.log(`  New daysOfWeek: ${JSON.stringify(newDays)}`);
      
      await prisma.timeBasedPromotion.update({
        where: { id: promo.id },
        data: { daysOfWeek: newDays }
      });
      updatedCount++;
    }
  }

  console.log(`Successfully updated ${updatedCount} promotion(s).`);
  
  const finalPromos = await prisma.timeBasedPromotion.findMany();
  console.log('Final promotions in database:');
  console.log(JSON.stringify(finalPromos, null, 2));
}

main()
  .catch(console.error)
  .finally(() => prisma.$disconnect());
