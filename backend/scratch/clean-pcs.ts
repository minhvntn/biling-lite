import { PrismaClient } from '@prisma/client';

const prisma = new PrismaClient();

async function main() {
  console.log('Connecting to database...');
  
  // Find all PCs
  const pcs = await prisma.pc.findMany({
    orderBy: { name: 'asc' },
  });
  
  console.log(`Found ${pcs.length} PCs in the database.`);
  if (pcs.length === 0) {
    console.log('No PCs found. Nothing to delete.');
    return;
  }
  
  for (const pc of pcs) {
    console.log(`- ${pc.name} (agentId: ${pc.agentId}, id: ${pc.id})`);
  }

  const pcIds = pcs.map(p => p.id);

  console.log('Deleting associated event logs...');
  const deletedEventLogs = await prisma.eventLog.deleteMany({
    where: { pcId: { in: pcIds } },
  });
  console.log(`Deleted ${deletedEventLogs.count} associated event logs.`);

  console.log('Deleting associated commands...');
  const deletedCommands = await prisma.command.deleteMany({
    where: { pcId: { in: pcIds } },
  });
  console.log(`Deleted ${deletedCommands.count} associated commands.`);

  console.log('Deleting associated sessions...');
  const deletedSessions = await prisma.session.deleteMany({
    where: { pcId: { in: pcIds } },
  });
  console.log(`Deleted ${deletedSessions.count} associated sessions.`);

  console.log('Deleting associated PC service orders...');
  const deletedOrders = await prisma.pcServiceOrder.deleteMany({
    where: { pcId: { in: pcIds } },
  });
  console.log(`Deleted ${deletedOrders.count} associated service orders.`);

  console.log('Deleting PCs...');
  const deletedPcs = await prisma.pc.deleteMany();
  console.log(`Deleted ${deletedPcs.count} PCs from the database.`);
  console.log('Cleanup completed successfully!');
}

main()
  .catch((e) => {
    console.error('Error during cleanup:', e);
    process.exit(1);
  })
  .finally(async () => {
    await prisma.$disconnect();
  });
